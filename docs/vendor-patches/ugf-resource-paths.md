# UGF initialization-time resource paths

Baseline: UnityGameFramework commit `e7eb3ef11393df0ac504204c480cdb839752b05f`, GameFramework `2021.05.31`.

The only vendor change is `ResourceComponent.cs`. `SetCustomResourcePaths(string readOnlyPath, string readWritePath)` stores two nonempty paths before `Start`. `Start` applies them at the original path-setting location, before `SetResourceMode`, helpers and load agents. Its first statement locks the setter, including unsuccessful initialization; later calls throw `InvalidOperationException` before changing either value. Without a call, the original StreamingAssets and configured temporary/persistent path selection remains in effect. No resource manager, serializer, version-table, or Bundle format changes.

The project-owned `ResourceSlotBootstrap` takes an inactive GameRoot prefab. It validates a native Package table, creates `<slot>/Scratch`, injects `<slot>/Resources` and `<slot>/Scratch`, activates the root, then initializes native Package resources after ResourceComponent.Start. Activation is restricted to once per process. Full process restart is the version-switch boundary. GameRoot uses the official default Package mode; Bootstrap validates it before InitResources.

`ResourceService.LoadTextAsync` is F2's narrow TextAsset consumer: asynchronous native GF asset load, copied text and paired unload. F3 will supply the shared resource interface; there is no dependency on future contracts here.

`ResourceSlotTests` exercises the real native manager. A test-only helper observes both selected paths and Package mode before any load agent exists; another helper observes agent creation; the native version-table read observes all three agents. These boundary observations establish configured state before native initialization, without adding vendor test callbacks or substituting a fake manager. Separate tests reject late mutation and verify the root remains unchanged.

Run `./Tool/Client/Test-Client.ps1 -Task F2 -Mode PlayMode`, then `./Tool/Client/Test-ResourceSlots.ps1 -Platform Windows64`. The proof builder uses official ResourceCollection and ResourceBuilderController APIs and isolated configuration attributes. It creates A/B complete Package outputs with the same asset address and different text. The single Windows IL2CPP Player is cold-started twice. Each process archives its root, native version-table SHA-256, marker, late-change rejection and natural exit; the harness verifies identical EXE/GameAssembly hashes and snapshots both read-only resource trees before/after each process.

The proof runtime is a separate `Client.ResourceSlot.Proof` assembly guarded by `UNITY_EDITOR || RESOURCE_SLOT_PROOF`. Editor access allows creation of the temporary proof scene; only the explicit proof BuildPlayerOptions supplies the Player define. The normal Bootstrap scene has no proof component. Normal Player builds exclude the assembly and its command line/marker handling. Generated marker assets and build evidence remain under ignored test/build directories. The Bootstrap scene and its AOT components do not reference the marker asset.

On vendor upgrades, reapply only this patch and rerun both gates. Never infer Package compatibility from AssetDatabase or live root changes.
