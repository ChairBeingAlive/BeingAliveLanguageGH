# GSP Extensions - How to Add Your Own C++ Functions

This folder contains project-specific extension functions that bridge C# and C++
code using the GeoSharPlus library.

## Architecture Overview

```
Your C# Code
     ↓
┌─────────────────────────────────────────────────────────────┐
│  GSP.Core.Serializer          - Converts types to byte[]   │
│  GSP.Core.MarshalHelper       - Handles memory marshaling  │
│  GSP.Core.Platform            - OS detection, library paths│
└─────────────────────────────────────────────────────────────┘
     ↓
┌─────────────────────────────────────────────────────────────┐
│  GSP.Extensions.Bal.BalBridge - P/Invoke declarations      │
│  GSP.Extensions.Bal.BalUtils  - Clean API for users        │
└─────────────────────────────────────────────────────────────┘
     ↓
┌─────────────────────────────────────────────────────────────┐
│  GeoSharPlusCPP.dll           - C++ implementation         │
└─────────────────────────────────────────────────────────────┘
```

## Folder Structure

```
Extensions/
├── _README.md              # This file
└── bal/                    # BeingAliveLanguage extensions
    ├── BalBridge.cs        # P/Invoke declarations
    └── BalUtils.cs         # High-level wrapper functions
```

## Step-by-Step: Adding a New C++ Function

### Step 1: Write the C++ Function

In `GeoSharPlusCPP/src/Extensions/bal/BridgeAPI.cpp`:

```cpp
GSP_API bool GSP_CALL my_custom_function(
    const uint8_t* inBuffer, int inSize,
    uint8_t** outBuffer, int* outSize) {

    *outBuffer = nullptr;
    *outSize = 0;

    // Deserialize input
    std::vector<GeoSharPlusCPP::Vector3d> points;
    if (!GS::deserializePointArray(inBuffer, inSize, points)) {
        return false;
    }

    // Your algorithm here...

    // Serialize output
    if (!GS::serializePointArray(points, *outBuffer, *outSize)) {
        return false;
    }

    return true;
}
```

### Step 2: Add P/Invoke in BalBridge.cs

```csharp
[DllImport(Platform.WindowsLib, EntryPoint = "my_custom_function",
           CallingConvention = CallingConvention.Cdecl)]
private static extern bool MyCustomFunctionWin(
    byte[] inBuffer, int inSize, out IntPtr outBuffer, out int outSize);

[DllImport(Platform.MacLib, EntryPoint = "my_custom_function",
           CallingConvention = CallingConvention.Cdecl)]
private static extern bool MyCustomFunctionMac(
    byte[] inBuffer, int inSize, out IntPtr outBuffer, out int outSize);

public static bool MyCustomFunction(
    byte[] inBuffer, int inSize, out IntPtr outBuffer, out int outSize) {
    if (Platform.IsWindows)
        return MyCustomFunctionWin(inBuffer, inSize, out outBuffer, out outSize);
    else
        return MyCustomFunctionMac(inBuffer, inSize, out outBuffer, out outSize);
}
```

### Step 3: Add High-Level Wrapper in BalUtils.cs

```csharp
public static List<Point3d>? ProcessPoints(List<Point3d> points) {
    var buffer = RhinoAdapter.ToBuffer(points.ToArray());

    if (!BalBridge.MyCustomFunction(buffer, buffer.Length,
            out IntPtr outPtr, out int outSize))
        return null;

    var resultBuffer = MarshalHelper.CopyAndFree(outPtr, outSize);
    return RhinoAdapter.PointArrayFromBuffer(resultBuffer).ToList();
}
```

## Available Utilities

### GSP.Core.Platform

- `Platform.IsWindows` / `Platform.IsMacOS` - OS detection
- `Platform.WindowsLib` / `Platform.MacLib` - Library names for DllImport

### GSP.Core.MarshalHelper

- `CopyAndFree(IntPtr, int)` - Copy from unmanaged and free
- `Copy(IntPtr, int)` - Copy without freeing
- `Free(IntPtr)` - Free unmanaged memory

### GSP.Core.Serializer

- Platform-independent serialization (double[][], int[][])

### GSP.Adapters.Rhino.RhinoAdapter

- `ToBuffer(Point3d)` / `PointFromBuffer(byte[])`
- `ToBuffer(Point3d[])` / `PointArrayFromBuffer(byte[])`
- `ToBuffer(Mesh)` / `MeshFromBuffer(byte[])`
