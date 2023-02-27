#include "pch.h" // make sure this is on the top

#include "cppFunc.h"

void BAL_possionDiskElimSample(ON_SimpleArray<float>* inPt, int n,
	ON_3fPointArray* outPt) {
	// input conversion
	int sz = inPt->Count() / 3;

	std::vector<cy::Vec3f> inputPoints(sz);
	for (size_t i = 0; i < sz; i++) {
		inputPoints.emplace_back(*inPt->At(i * 3), *inPt->At(i * 3 + 1),
			*inPt->At(i * 3 + 2));
	}

	// elimination to the given number of pts
	cy::WeightedSampleElimination<cy::Vec3f, float, 3, int> wse;
	std::vector<cy::Vec3f> outputPoints(n);
	wse.Eliminate(inputPoints.data(), inputPoints.size(), outputPoints.data(),
		outputPoints.size());

	// output conversion
	outPt->Empty();
	for (auto p : outputPoints) {
		outPt->Append(ON_3fPoint(p.x, p.y, p.z));
	}
}
