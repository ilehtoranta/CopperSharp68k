# MUITaskList

`MUITaskList` is a larger MUI sample that uses a private MUI custom class and a
callback hook.

The application class is created with `MUI_CreateCustomClass()` and receives a
native dispatcher exported from C#. The task list uses a `struct Hook` display
callback to fill a multi-column MUI list from raw APTR-backed entry records.

Build:

```powershell
dotnet build .\Sdk.Amiga\Examples\MUITaskList\MUITaskList.csproj
```
