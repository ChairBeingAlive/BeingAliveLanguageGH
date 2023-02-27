#pragma once
#include "cyPointCloud.h"
#include "cySampleElim.h"
#include "cyVector.h"
#include "pch.h"

#if defined(RH_DLL_EXPORTS)

#define RH_CPP_CLASS __declspec(dllexport)
#define RH_CPP_FUNCTION __declspec(dllexport)
#define RH_CPP_DATA __declspec(dllexport)

#define RH_C_FUNCTION extern "C" __declspec(dllexport)

#else
#define RH_CPP_CLASS __declspec(dllimport)
#define RH_CPP_FUNCTION __declspec(dllimport)
#define RH_CPP_DATA __declspec(dllimport)

#define RH_C_FUNCTION extern "C" __declspec(dllimport)

#endif

// ! Sampling
RH_C_FUNCTION
void BAL_possionDiskElimSample(ON_SimpleArray<float>* inPt, int n,
                               ON_3fPointArray* outPt);
