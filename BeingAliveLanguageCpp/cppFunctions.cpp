#include "cppFunctions.hpp"

#include "cyCodeBase/cyPointCloud.h"
#include "cyCodeBase/cySampleElim.h"
#include "cyCodeBase/cyVector.h"

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call,
                      LPVOID lpReserved) {
  switch (ul_reason_for_call) {
    case DLL_PROCESS_ATTACH:
    case DLL_THREAD_ATTACH:
    case DLL_THREAD_DETACH:
    case DLL_PROCESS_DETACH:
      break;
  }
  return TRUE;
}

void BAL_possionDiskSample(ON_3fPointArray* inPt, int n,
                           ON_3fPointArray* outPt) {
  // input conversion
  std::vector<cy::Vec3f> inputPoints;
  for (size_t i = 0; i < inPt->Count(); i++) {
    inputPoints.emplace_back(inPt->At(i)->x, inPt->At(i)->y, inPt->At(i)->z);
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
