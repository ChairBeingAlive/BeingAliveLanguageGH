#include "stdafx.h"
#include "cppFunc.h"

void BAL_possionDiskElimSample(ON_SimpleArray<float>* inPt, int n,
	ON_3dPointArray* outPt) {
	// input conversion
	int sz = inPt->Count() / 3;

	std::vector<cy::Vec3f> inputPoints(0);
	for (size_t i = 0; i < sz; i++) {
		inputPoints.emplace_back(*inPt->At(i * 3), *inPt->At(i * 3 + 1),
			*inPt->At(i * 3 + 2));
	}

	// elimination to the given number of pts
	cy::WeightedSampleElimination<cy::Vec3f, float, 2> wse;
	std::vector<cy::Vec3f> outputPoints(n);
	wse.Eliminate(inputPoints.data(), inputPoints.size(), outputPoints.data(),
		outputPoints.size(), true);

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
