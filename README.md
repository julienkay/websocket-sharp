## Summary ##

This is a fork of [websocket-sharp](https://github.com/sta/websocket-sharp).

The intent of this project is to improve behaviour when using websockets in enterprise applications, where a connection must be established from behind a proxy server that uses some form of authentication.

In summary, the main goals are:
- Improved support for authentication during proxy handshake
- Compatibility with Unity

## What this project is NOT ##

This is not intended to be a websocket library with long term support. This is only an interim solution until better websocket implementations are available for use in Unity. 

&rightarrow; Use .NETs [ClientWebsocket](https://docs.microsoft.com/en-us/dotnet/api/system.net.websockets.clientwebsocket) instead, unless you are:
- stuck on an old version of Mono (i.e. current versions of Unity) AND
- want your application to run in enterprise environments (behind proxy servers using authentication)

## Modifications ##

- [x] Workaround for [#530](https://github.com/sta/websocket-sharp/issues/530) (Error when proxy server offers multiple authentication methods)

## Roadmap ##

- [ ] Support Negotiate Authentication (Kerberos/NTLM)