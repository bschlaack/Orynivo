# Orynivo Server and MCP behind HTTPS

These examples put a proxy on the same machine as Orynivo Server and the desktop
MCP host. Replace `music.example.net` with your DNS name. The public server URL
is `https://music.example.net/orynivo`; enter that complete base URL in the
desktop server connection. MCP clients use `https://music.example.net/mcp`.

Orynivo Server listens on port 5280 by default. Bind it to loopback when only
the local proxy should reach it, for example with
`ASPNETCORE_URLS=http://127.0.0.1:5280`. Restrict upstream ports with a
firewall. For proxies on another machine, replace loopback with a private
upstream address and protect that connection with a trusted private network, VPN
or upstream TLS.

For MCP, enable **Allow access from the local network** in Settings and
configure the displayed bearer token in the client. This enables Orynivo's
authentication middleware even when the proxy is on the same host. Keep port
49200 inaccessible from untrusted networks. Proxying a default unauthenticated
loopback MCP host to the internet would expose its enabled tools without the
bearer-token check.

## Caddy

This Caddyfile obtains a public certificate automatically when DNS points to the
proxy and the necessary certificate-challenge ports are reachable:

```caddyfile
{
    log {
        output discard
    }
}

music.example.net {
    log {
        output discard
    }

    handle_path /orynivo/* {
        reverse_proxy 127.0.0.1:5280 {
            flush_interval -1
        }
    }

    @mcp path /mcp /mcp/*
    handle @mcp {
        reverse_proxy 127.0.0.1:49200 {
            flush_interval -1
        }
    }

    handle {
        respond 404
    }
}
```

`handle_path` removes `/orynivo` before forwarding, while the MCP handler
retains `/mcp`. Caddy forwards request headers and queries by default, so
`X-Api-Key`, `Authorization`, range headers, profile selectors and query
authentication reach Orynivo. The negative flush interval forwards streaming
data promptly. No short upstream response timeout is added. Validate with
`caddy validate --config /etc/caddy/Caddyfile` before reloading your service.

## nginx

Obtain a certificate and private key for the public hostname first, and replace
the two `/etc/ssl/orynivo/` paths below. Place this server block inside nginx's
`http` context:

```nginx
server {
    listen 443 ssl;
    server_name music.example.net;
    ssl_certificate /etc/ssl/orynivo/fullchain.pem;
    ssl_certificate_key /etc/ssl/orynivo/privkey.pem;
    ssl_protocols TLSv1.2 TLSv1.3;

    access_log off;
    error_log /dev/null;
    client_max_body_size 100m;

    proxy_http_version 1.1;
    proxy_set_header Host $host;
    proxy_set_header Connection "";
    proxy_set_header X-Api-Key $http_x_api_key;
    proxy_set_header Authorization $http_authorization;
    proxy_buffering off;
    proxy_request_buffering off;
    proxy_cache off;
    proxy_read_timeout 3600s;
    proxy_send_timeout 3600s;

    location /orynivo/ {
        proxy_pass http://127.0.0.1:5280/;
    }

    location = /mcp {
        proxy_pass http://127.0.0.1:49200;
    }

    location /mcp/ {
        proxy_pass http://127.0.0.1:49200;
    }

    location / {
        return 404;
    }
}
```

The trailing slash on the server `proxy_pass` strips `/orynivo/`; the MCP URLs
retain their original path. Queries remain intact. Buffering and caching are
disabled for immediate streaming/SSE delivery. Timeouts are idle intervals
between reads/writes, not a one-hour limit on a continuously active stream.
Adjust the upload limit if you use larger library import archives. Run
`nginx -t` before reloading. The examples deliberately do not redirect missing
trailing slashes; use `/orynivo/api/...` for API requests.

## Authentication and testing

Use a current Orynivo client. Server API calls require `X-Api-Key`; generated
audio and artwork requests also support the `key` query parameter. MCP uses
`Authorization: Bearer`, not the server API key. Never insert a real credential
into proxy configuration, a support report, shell history, a screenshot or a
model prompt. These examples discard proxy request and error logs because error
records can also contain authenticated request URLs. Re-enable diagnostics only
with suitable redaction. Disable or redact query/header logging in any load
balancer or monitoring service in front of these proxies as well.

Before opening external access, check all of the following with disposable test
credentials:

- `GET /orynivo/api/health` reaches the server without authentication.
- A protected catalog request fails without a key and succeeds with the header;
  repeat with the query form without recording that URL.
- Audio range requests retain `Range`, status 206, `Content-Range` and exact
  bytes. A wrong key still fails through the proxy.
- The MCP client initializes, lists permitted tools and receives streaming
  responses through `/mcp`. Missing or wrong bearer tokens fail. Supply any MCP
  session/protocol headers returned by initialization on later requests.
- Stop a stream and confirm the upstream connection closes; test seeking and a
  longer listening session. Do not replay non-idempotent tool calls on errors.

TLS terminates at the proxy. The loopback hop is HTTP; a remote upstream hop
needs its own protection. On a fully trusted LAN, direct HTTP can be an explicit
choice and a proxy is optional, but credentials and media remain unencrypted on
that hop. For remote administration and playback, a VPN can avoid public
exposure. Keep normal server and MCP authentication enabled in either setup.

## Verification scope

On Windows, task-local Caddy 2.11.7 and nginx 1.30.6 passed synthetic TLS proxy
checks on 2026-10-19: actual example route blocks with substituted loopback
ports/test certificates preserved the prefix/query, both authentication forms,
unauthorized responses, range status/header/bytes and SSE delivery before
upstream completion. nginx's `/dev/null` log sink used Windows `NUL` in the
fixture. No personal backend or credential was used. These tests do not run the
actual Orynivo endpoints, certificate issuance, reconnection or a long session.

Configuration and acceptance recipes do not establish a production deployment.
Human end-to-end testing with Orynivo, both proxies and a real MCP client is
still required for issue #180. Record OS, proxy version, key/header checks,
seeking, streaming and the MCP client/transport in that report. Do not include
credentials or authenticated URLs.

References:

- [Caddy handle_path](https://caddyserver.com/docs/caddyfile/directives/handle_path)
- [Caddy reverse_proxy](https://caddyserver.com/docs/caddyfile/directives/reverse_proxy)
- [nginx proxy module](https://nginx.org/en/docs/http/ngx_http_proxy_module.html)
