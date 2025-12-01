# Copilot Instructions for BeingAliveLanguage

## Project Overview

**BeingAliveLanguage** is a multi-project C# solution that creates a Grasshopper plugin for Rhino 3D. The plugin implements tools for translating and visualizing soil-related information, including soil water content, soil-root interaction, soil horizons, and tree/vegetation modeling.

## Solution Structure

This is a **multi-project solution** with the following architecture:

```
BeingAliveLanguageGH/
├── BeingAliveLanguage/          # Main Grasshopper plugin project (C#)
│   ├── BAL*.cs                  # Component definitions
│   ├── BALCore/                 # Core algorithms and data structures
│   ├── Properties/              # Resources (icons, etc.)
│   └── Resources/               # Asset files
├── GeoSharPlusCPP/              # C++ computational library
│   ├── include/                 # Header files
│   ├── src/                     # C++ source files
│   └── CMakeLists.txt           # CMake build configuration
├── GeoSharPlusNET/              # .NET wrapper for C++/C# interop
│   ├── NativeBridge.cs          # P/Invoke declarations
│   ├── Wrapper.cs               # High-level wrapper classes
│   └── BalFuncWrapper.cs        # Function-specific wrappers
├── _release/                    # Release packaging scripts and archives
└── beingAliveLanguage.sln       # Visual Studio solution file
```

## Project Roles

### BeingAliveLanguage (Main Project)

- **Purpose**: The main Grasshopper plugin containing all GH_Component implementations
- **Language**: C#
- **Framework**: .NET Framework targeting Grasshopper/Rhino
- **Key Files**:
  - `BALtree.cs` - Tree and forest modeling components
  - `BALsoil.cs` - Soil visualization components
  - `BALroot.cs` - Root system modeling
  - `BALclimate.cs` - Climate-related components
  - `BALCore/` - Core classes like `Tree3D`, `Utils`, `BranchNode3D`

### GeoSharPlusCPP (C++ Library)

- **Purpose**: High-performance computational geometry operations
- **Language**: C++
- **Build System**: CMake with vcpkg for dependencies
- **Role**: Provides native implementations for performance-critical algorithms

### GeoSharPlusNET (.NET Wrapper)

- **Purpose**: Bridge between C# and C++ code
- **Language**: C#
- **Role**: Contains P/Invoke declarations and wrapper classes to call native C++ functions from the managed C# code

## Development Guidelines

### Code Style

- Use C# conventions for the main project
- Components inherit from `GH_Component`
- Each component has:
  - Constructor with name, nickname, description, category, subcategory
  - `Icon` property returning a bitmap
  - `ComponentGuid` property with unique GUID
  - `RegisterInputParams` and `RegisterOutputParams` methods
  - `SolveInstance` method for main logic

### Component Categories

- `BAL` is the main category
- Subcategories include: `01::soil`, `02::root`, `03::plant`, `04::climate`, etc.

### Key Classes

- `Tree3D` - 3D tree representation with growth phases and branch structure
- `Tree3DWrapper` - Grasshopper wrapper for Tree3D objects
- `BranchNode3D` - Individual branch node in tree structure
- `Utils` - Utility functions including geometry helpers

### Tree Growth System

Trees grow in 4 stages controlled by phase parameter:

1. **Stage 1 (phase 1-4)**: Young tree, trunk growth with initial side branches
2. **Stage 2 (phase 5-8)**: Mature tree, branching and crown development
3. **Stage 3 (phase 9-11)**: Aging tree, continued growth
4. **Stage 4 (phase 12)**: Dying tree, branch removal

### Forest Interaction System

- `BALtreeInteraction` component handles tree-to-tree interactions
- Trees scale their crowns based on proximity to neighbors
- Uses KD-tree for nearest neighbor queries
- Scaling affects branches facing toward neighboring trees

## Building

### Prerequisites

- Visual Studio with .NET Framework support
- Rhino 7+ and Grasshopper SDK
- CMake (for C++ project)
- vcpkg (for C++ dependencies)

### Build Order

1. Build `GeoSharPlusCPP` (CMake)
2. Build `GeoSharPlusNET`
3. Build `BeingAliveLanguage`

## Testing

- Test files and example Grasshopper definitions should be used to verify component behavior
- Pay attention to edge cases like overlapping trees, single tree scenarios, etc.

## Release

- Release scripts are in `_release/`
- Packages are created as `.yak` files for Rhino Package Manager
- Version naming follows: `beingalivelanguage-{version}-rh{rhino_version}-any.yak`
