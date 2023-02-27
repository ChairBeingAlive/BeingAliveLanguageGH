#pragma once
#include "cyPointCloud.h"
#include "cySampleElim.h"
#include "cyVector.h"
#include "pch.h"

#define RH_C_FUNCTION extern "C" __declspec(dllexport)

// ! Sampling
RH_C_FUNCTION
void BAL_possionDiskElimSample(ON_SimpleArray<float>* inPt, int n,
	ON_3fPointArray* outPt);
