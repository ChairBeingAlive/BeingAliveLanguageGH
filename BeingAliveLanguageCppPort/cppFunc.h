
#pragma once
#include "cyPointCloud.h"
#include "cySampleElim.h"
#include "cyVector.h"
#include "stdafx.h"

#define RH_C_FUNCTION extern "C" __declspec(dllexport)

// ! Sampling
RH_C_FUNCTION
void BAL_possionDiskElimSample(ON_SimpleArray<float>* inPt, int n,
	ON_3dPointArray* outPt);

RH_C_FUNCTION
double BAL_Addition(double a, double b);
