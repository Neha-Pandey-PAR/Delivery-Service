# HMAC Authentication — Signing & Verification Spec

The PJI Delivery Event Service authenticates every request via an HMAC-SHA256 signature placed in the `Authorization` header. This document describes the exact wire format so any client (Postman, curl, .NET, JS, Python) can produce a request the service will accept.

---

## 1. Authorization header format

```
Authorization: HMAC <clientName>:<keyId>:<timestamp>:<nonce>:<signature>
```

Five colon-separated fields after the literal `HMAC ` scheme prefix:

| Field | Meaning | Example |
|---|---|---|
| `clientName` | Logical client identifier registered in the service's `HmacClients` config (also used as the `client_name` claim once authenticated) | `PJI` |
| `keyId` | Identifies which key inside that client's key array is being used (lets clients rotate without coordination) | `pji-key-v1` |
| `timestamp` | Unix epoch **seconds** (integer, not milliseconds) at request time | `1735574400` |
| `nonce` | Per-request unique string. UUIDv4 is recommended; the server doesn't enforce uniqueness but it's good hygiene against replay caching | `9b3d4f2c-...` |
| `signature` | Lowercase hex of HMAC-SHA256 over the signing string (next section) | `c1f4a8…b21e` |

---

## 2. The signing string

Five lines joined by **`\n`** (LF only — never CRLF):

```
{HTTP_METHOD}
{REQUEST_PATH_WITH_QUERY}
{TIMESTAMP_UNIX_SECONDS}
{NONCE}
{SHA256_HEX_LOWERCASE_OF_BODY}
```

Concrete example (request `POST /v1/orders/12345/events/dropped-off` with body `{"eventId":"x"}`):

```
POST
/v1/orders/12345/events/dropped-off
1735574400
9b3d4f2c-aa01-4b6c-9e72-d5a1c9e2f000
6c54d1e0a3b...   ← sha256 hex of the body
```

### Field rules

| Field | Rule |
|---|---|
| **Method** | Uppercase: `POST`, `GET`, `PUT`, `DELETE`. Don't send `Post`. |
| **Path-with-query** | Must start with `/`. Include the query string with leading `?` if present, otherwise omit it (no trailing `?`). Do NOT include scheme/host/port. |
| **Timestamp** | Unix **seconds** (10-digit integer). Must be within ±300 s of server time. |
| **Nonce** | Any non-empty string. Must not contain `:` or `\n` (it would break header parsing). |
| **Body hash** | SHA-256 of the **exact bytes** sent in the HTTP body. Empty body → SHA-256 of zero bytes = `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855`. Lowercase hex. |
| **Separator** | LF (`\n`, byte `0x0A`). Not CRLF. Not OS-specific. |

---

## 3. Computing the signature

```
secretBytes = base64Decode(keyConfig.Secret)        // 32-byte raw key
mac         = HMAC-SHA256(secretBytes, signingString_as_utf8_bytes)
signature   = hex(mac).toLowerCase()                // 64-char lowercase hex
```

Notes:
- **Key encoding:** The `Secret` field in Secrets Manager / config is **base64-encoded**. Decode it to raw bytes before passing to HMAC. Do not pass the base64 string itself as the HMAC key.
- **Output encoding:** lowercase hex. The server lowercases both sides before constant-time comparison, so case won't actually break it — but stick with lowercase for predictability.
- **Hash family:** SHA-256. No other hash is supported.

---

## 4. Server-side verification (what the service does)

`HmacAuthenticationHandler.HandleAuthenticateAsync`:

1. Read `Authorization` header → require `HMAC ` prefix → split on `:` (must be exactly 5 parts).
2. Parse `timestamp` as `long`. `Math.Abs(now - timestamp) > 300` → fail with `Timestamp expired or in future`.
3. Look up `Clients[clientName]` → fail with `Unknown client` if absent.
4. Find `Keys[*]` where `KeyId == keyId && IsActive == true` → fail with `Unknown or inactive key`.
5. `Request.EnableBuffering()`, read full body bytes, rewind stream. SHA-256 the bytes; lowercase hex.
6. Build the same signing string (method, `Request.Path + Request.QueryString`, timestamp, nonce, body-hash).
7. HMAC-SHA256 with the base64-decoded key secret; lowercase hex.
8. `CryptographicOperations.FixedTimeEquals(provided, computed)` (constant-time comparison) → fail with `Invalid signature` on mismatch.
9. On success, attach `ClaimsPrincipal` with claim `client_name = <clientName>`.

Any failure produces a `401` `application/problem+json` response whose `detail` and `errors.message` fields carry the exact failure reason — useful for debugging.

---

## 5. Common gotchas (debugging "Invalid signature")

In order of likelihood:

1. **Path mismatch.** Most common. Build the path the same way the server sees it: leading `/`, raw query string, no host. Postman: use `pm.request.url.getPath()` (already has the leading slash) — do **not** prepend an extra `/`.
2. **Body mismatch.** Hash the bytes that actually go on the wire, not the template. If your client substitutes `{{variables}}` after you compute the signature, the signature won't match. Resolve variables first, then hash.
3. **Timestamp drift.** Keep your local clock within 5 minutes of UTC. Quick check: `Math.floor(Date.now() / 1000)` — must be within 300 of `date +%s` on the server.
4. **Wrong unit on timestamp.** `Date.now()` returns **milliseconds** in JS — divide by 1000 for seconds.
5. **Method case.** Uppercase the method.
6. **Wrong key encoding.** The `Secret` is base64. Decode to bytes; don't HMAC with the base64 string.
7. **Wrong separator.** Use `\n`, not `\r\n`. Don't let your IDE save the signing string with CRLF line endings.
8. **Empty body but you hashed `null`.** Empty body must hash empty bytes, not the literal string `"null"`. Result: `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855`.
9. **Inactive key.** `IsActive: false` keys are rejected even if everything else matches — useful for retiring a key without deleting it.

---

## 6. Reference implementations

### .NET (server already does this; client-side example)

```csharp
using System.Security.Cryptography;
using System.Text;

string method = "POST";
string path = "/v1/orders/12345/events/dropped-off";
long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
string nonce = Guid.NewGuid().ToString();
byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
string bodyHash = Convert.ToHexString(SHA256.HashData(bodyBytes)).ToLowerInvariant();

string signingString = string.Join('\n', method, path, ts, nonce, bodyHash);
byte[] secret = Convert.FromBase64String("VGhpc0lzQTMyQnl0ZVNlY3JldEZvckhtYWNUZXN0cyE=");
string signature = Convert.ToHexString(
    HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(signingString))
).ToLowerInvariant();

string auth = $"HMAC PJI:pji-key-v1:{ts}:{nonce}:{signature}";
```

### JavaScript (Postman / Node)

```javascript
const CryptoJS = require('crypto-js');

const ts = Math.floor(Date.now() / 1000).toString();
const nonce = crypto.randomUUID();
const method = 'POST';
const path = '/v1/orders/12345/events/dropped-off';
const body = JSON.stringify({ eventId: 'x', locationNumber: 12345, isInternal: false });

const bodyHash = CryptoJS.SHA256(body).toString(CryptoJS.enc.Hex);
const signingString = [method, path, ts, nonce, bodyHash].join('\n');

const secret = CryptoJS.enc.Base64.parse('VGhpc0lzQTMyQnl0ZVNlY3JldEZvckhtYWNUZXN0cyE=');
const signature = CryptoJS.HmacSHA256(signingString, secret).toString(CryptoJS.enc.Hex);

const auth = `HMAC PJI:pji-key-v1:${ts}:${nonce}:${signature}`;
```

### curl one-liner (Linux/macOS)

```bash
SECRET_HEX=$(echo -n "VGhpc0lzQTMyQnl0ZVNlY3JldEZvckhtYWNUZXN0cyE=" | base64 -d | xxd -p -c 256)
TS=$(date +%s)
NONCE=$(uuidgen)
BODY='{"eventId":"x","locationNumber":12345,"isInternal":false}'
BODY_HASH=$(echo -n "$BODY" | openssl dgst -sha256 | awk '{print $2}')
SIGNING_STRING=$(printf "POST\n/v1/orders/12345/events/dropped-off\n%s\n%s\n%s" "$TS" "$NONCE" "$BODY_HASH")
SIGNATURE=$(echo -n "$SIGNING_STRING" | openssl dgst -sha256 -mac HMAC -macopt hexkey:"$SECRET_HEX" | awk '{print $2}')

curl -X POST http://localhost:5000/v1/orders/12345/events/dropped-off \
  -H "Authorization: HMAC PJI:pji-key-v1:$TS:$NONCE:$SIGNATURE" \
  -H "Content-Type: application/json" \
  -d "$BODY"
```

---

## 7. Test vector

Use this exact input to verify any client implementation matches the server.

| Field | Value |
|---|---|
| Method | `POST` |
| Path | `/v1/orders/12345/events/dropped-off` |
| Timestamp | `1735574400` |
| Nonce | `test-nonce-1` |
| Body | `{"eventId":"evt-abc","locationNumber":12345,"isInternal":false}` |
| Body SHA-256 (hex) | `4d3e72ab0a3c1d9f2b1e8c7a6b5d4e3c2a1908f7e6d5c4b3a29180706f5e4d3c` *(compute & compare)* |
| Secret (base64) | `VGhpc0lzQTMyQnl0ZVNlY3JldEZvckhtYWNUZXN0cyE=` |
| Expected signature | *(compute & compare against the server)* |

To verify: build your signing string, HMAC it, and the resulting hex must match what the server computes from the same inputs. If your client and the server agree on this vector, the only remaining variables are clock skew and what your client sends as the body bytes.
