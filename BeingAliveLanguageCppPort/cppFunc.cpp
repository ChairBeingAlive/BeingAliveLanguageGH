#include "stdafx.h"
#include "cppFunc.h"

void BAL_possionDiskElimSample(ON_SimpleArray<double>* inPt, double area, int n,
	ON_3dPointArray* outPt) {
	// input conversion
	int sz = inPt->Count() / 3;

	std::vector<cy::Vec3d> inputPoints(0);
	for (size_t i = 0; i < sz; i++) {
		inputPoints.emplace_back(*inPt->At(i * 3), *inPt->At(i * 3 + 1),
			*inPt->At(i * 3 + 2));
	}

	// elimination to the given number of pts
	cy::WeightedSampleElimination<cy::Vec3d, double, 3> wse;
	std::vector<cy::Vec3d> outputPoints(n);

	//! Important!
	// d_max is used to define the sampling dist param based on sampling area
	//http://www.cemyuksel.com/cyCodeBase/soln/poisson_disk_sampling.html
	float d_max = 2 * wse.GetMaxPoissonDiskRadius(2, outputPoints.size(), area);

	// 3D points, sampling in 2D plane
	wse.Eliminate(inputPoints.data(), inputPoints.size(), outputPoints.data(),
		outputPoints.size(), false, d_max, 2);

	// output conversion
	outPt->Empty();
	for (auto p : outputPoints) {
		outPt->Append(ON_3dPoint(p.x, p.y, p.z));
	}
}

double BAL_Addition(double a, double b)
{
	return a + b;
}
