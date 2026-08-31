.NET type and function generator for C++ games, powered by [SharpPdb](https://github.com/southpolenator/SharpPdb).

This generator requires a PDB file for the C++ game, so requires some developer support,
either for the developer to provide their PDB file, or generate the bindings themselves.

This project was created to produce a C# bindings DLL for 
[MIO: Memories In Orbit](https://store.steampowered.com/app/1672810/MIO_Memories_in_Orbit/) 
([GOG](https://www.gog.com/en/game/mio_memories_in_orbit)). 
As such, some of the features of this project are naive, or hardcoded to handle cases specific to MIO. 
Some changes will be required to adapt this project to other C++ games.

---

This project currently generates .cs files, rather than a compiled dll. 
Running the generator for a 100MB PDB may take around 10 seconds, 
while compiling the output files may take closer to a minute.

To compile, create a new project, and paste the output folders into the project.
- The project requires a dependency to [PolyHook2.Net](https://www.nuget.org/packages/PolyHook2.NET/)

---

## Structure of a generated project

For this example, the namespace is assumed to be `MyGame`

| Path | Description |
| -------- | ------- |
| NativeMod/ | Contains types used for hooking, and to store the .exe's memory address which all functions and static fields use |
| MyGame/GlobalFields.cs | Contains all global fields in type MyGame.Globals<br/> - This type is a struct for easier debug inspection. |
| MyGame/GlobalFunctions.cs | Contains all global functions. <br/> - Functions full names look like `MyGame.GlobalFunctions.Path.To.File.Function()`<br/> - `Path.To.File` is the path for the original .cpp implementation<br/> - Functions with paths not easily resolved are all put in `GlobalFunctions.Functions`<br/> - Functions that lack a path (internal to C++(?)) are in `GlobalFunctions.Internals` |
| MyGame/Types/ | Contains all struct, union, and enum definitions <br/> - Files are split by namespace, or by base type |
| MyGame/Hooks/ | Contains generated hook classes for functions in `MyGame.Types/` |
| MyGame/GlobalHooks/ | Contains generated hook classes for functions in `MyGame.GlobalFunctions.cs` |

All types and functions have XmlDocs where the original C++ name is used. For example, the generated type `Array_int` is described as `Array<int>`.

For generated types, any functions which were fully inlined and had its function bodies removed are commented as such.

---

## Using in a ModLoader

A ModLoader **must** set `NativeMod.NativeModule.MemoryAddress` to the memory address of the game's executable
before any static initializers are run for any structs or functions.

---

## Generated Structs

Although C# does not allow struct inheritance, types are generated to mirror the C++ type.
If a C++ struct inherits one or more base types, the generated struct contains those base types (named `Base`, or `Base1`, `Base2`, ...), and all inherited fields that reference the base types.
Structs with virtual methods are supported, but not yet tested.

---

## Hooks

Both class methods and global functions are hooked. Hooks are stored paths similar to the original type or global function's full name.

| Class/Global | Hook? | Path |
| -------- | ------- |
| Class | No | `MyGame.InnerNs.MyType.Foo()` |
| Class | Yes | `On.MyGame.InnerNs.OnMyType.Foo` |
| Global | No | `MyGame.GlobalFunctions.Path.To.File.Foo()` |
| Global | Yes | `On.MyGame.GlobalFunctions.Path.To.On_File.Foo` |

Hook types are a static class which have events for Prefix, Suffix, and the main Hook.
- Prefixes do not use a return value.
- Suffixes may modify the return value.
- Hooks may modify arguments, the return value, and call the hooked C++ function 0 or more times.

---

## Unimplemented features

Many features that would be needed for a comfortable modding experience is missing.
- Constructors. Currently these are supported as
    ```csharp
    MyStruct myStruct = default;
    myStruct.Ctor(arg1, arg2);
    ```
- Inherited methods. Currently requires
    ```cs
    myStruct.Base.BaseMethod(arg1)
    myStruct.Base.Base.OtherBaseMethod(arg1)
    ```
- Modded inheritance. This has not been explored yet, but might be possible with some boilerplate.
- Allocating structs in native memory. This requires some manual tracking to free allocated structs, or in cases where a fixed amount are needed in a mod's lifetime, simply defining a static field for the struct.
