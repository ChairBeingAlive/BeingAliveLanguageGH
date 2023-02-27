// pch.h: This is a precompiled header file.
// Files listed below are compiled only once, improving build performance for
// future builds. This also affects IntelliSense performance, including code
// completion and many code browsing features. However, files listed here are
// ALL re-compiled if any one of them is updated between builds. Do not add
// files here that you will be updating frequently as this negates the
// performance advantage.

#ifndef PCH_H
#define PCH_H

#define NOMINMAX  // avoid the min/max macro from windows.h
// add headers that you want to pre-compile here
#include "framework.h"

using namespace std;
#include <vector>

// -------------------------------------
// Open Source OpenNURBS
// -------------------------------------
// defining OPENNURBS_PUBLIC_INSTALL_DIR enables automatic linking using pragmas
#define OPENNURBS_PUBLIC_INSTALL_DIR "C:/xLibraries/opennurbs"
// uncomment the next line if you want to use opennurbs as a DLL
#define OPENNURBS_IMPORTS
#include "C:/xLibraries/opennurbs/opennurbs_public.h"

#endif  // PCH_H
