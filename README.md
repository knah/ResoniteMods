A collection of [ResoniteModLoader](https://github.com/resonite-modding-group/ResoniteModLoader/) mods for [Resonite](https://resonite.com/).  

# DictEditorJank
Adds a very jank editor to `SyncDictionary` data model fields, such as `BipedRig.Bones` or `User.networkStats`.  
Also generates editors for `SyncVar`, found inside `User.networkStats`.  
It doesn't really let you edit the dictionary itself (add/remove elements), but it should let you inspect and maybe edit the contents at the very least.   
You also might be able to reference their members for use with flux, but why?  

# ResoQuicc
And experimental example implementation of QUIC networking for testing.  
Not usable to host sessions from home (there's no NAT traversal or relays), but should work for headless servers with public IP or LAN.  
You'll need to get the msquic library for your OS and put it in Resonite's root folder. The best source I can offer is [msquic repo CI](https://github.com/microsoft/msquic/actions/workflows/build.yml?query=branch%3Arelease%2F2.5).  
On Linux, you may try your OS package for `libmsquic` for the headless, if it exists. For Steam graphical client, you'll have to place `libmsquic.so` in game folder, together with its dependencies (`libxdp.so.1`, `libbpf.so.1`, `libcrypto.so.3`, `libns-3.so.200`, `libns-route-3.so.200`, `libnuma.so.1`).  
Be sure to check mod settings for config options to tune this for your specific experimental setup.  

## Installation
1. Install [ResoniteModLoader](https://github.com/resonite-modding-group/ResoniteModLoader/).
1. Place .dll files of desired mods into your `rml_mods` folder. Follow RML readme to locate it.
1. Start the game. If you want to verify that the mod is working you can check your Resonite logs.

## Building
To build this repository as-is, Resonite game folder (the one containing `Resonite.exe`) can be symlinked under `Resonite` next to the solution file.  
Alternatively, it can be copied to the same path.