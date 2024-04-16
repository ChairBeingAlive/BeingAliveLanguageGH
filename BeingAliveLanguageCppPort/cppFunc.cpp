#include "stdafx.h"
#include "cppFunc.h"

//#include <gmpxx.h>
//#include <igl/copyleft/progressive_hulls.h>

double BAL_Addition(double a, double b)
{
	return a + b;
}

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


//void BAL_computeHull(ON_3dPointArray* inPt, ON_3dPointArray* outPt, ON_SimpleArray<int>* outF) {
//	// input conversion
//	int sz = inPt->Count();
//	Eigen::MatrixXd V(sz, 3);
//	for (size_t i = 0; i < sz; i++) {
//		auto x = *inPt->At(i);
//		V.row(i) = Eigen::RowVector3d((*inPt->At(i)).x, (*inPt->At(i)).y, (*inPt->At(i)).z);
//	}
//
//	// compute hull
//	Eigen::MatrixXd newV;
//	Eigen::MatrixXi newF;
//	igl::copyleft::cgal::convex_hull(V, newV, newF);
//
//	// output conversion
//	outPt->Empty();
//	for (size_t i = 0; i < newV.rows(); i++) {
//		outPt->Append(ON_3dPoint(newV(i, 0), newV(i, 1), newV(i, 2)));
//	}
//
//	outF->Empty();
//	for (size_t i = 0; i < newF.rows(); i++) {
//		outF->Append(newF(i, 0));
//		outF->Append(newF(i, 1));
//		outF->Append(newF(i, 2));
//	}
//}
