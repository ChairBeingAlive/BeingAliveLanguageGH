
#pragma once
#include "cyPointCloud.h"
#include "cySampleElim.h"
#include "cyVector.h"
#include "stdafx.h"

// Windows build
#if defined (_WIN32)
#define RH_CPP_CLASS __declspec(dllexport)
#define RH_CPP_FUNCTION __declspec(dllexport)
#define RH_C_FUNCTION extern "C" __declspec(dllexport)
#endif

// Apple build
#if defined(__APPLE__)
#define RH_CPP_CLASS __attribute__ ((visibility ("default")))
#define RH_CPP_FUNCTION __attribute__ ((visibility ("default")))
#define RH_C_FUNCTION extern "C" __attribute__ ((visibility ("default")))
#endif // __APPLE__

// ! testing func for cpp/c# integration
RH_C_FUNCTION
double BAL_Addition(double a, double b);


// ! Sampling
// `generalArea`: area for 2D; volume for 3D
RH_C_FUNCTION
void BAL_possionDiskElimSample(ON_SimpleArray<double>* inPt, double generalArea, int n,
	ON_3dPointArray* outPt);


//RH_C_FUNCTION
//void BAL_computeHull(ON_3dPointArray* inPt, ON_3dPointArray* outPt, ON_SimpleArray<int>* outF);
