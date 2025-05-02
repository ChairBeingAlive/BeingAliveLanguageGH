#include "GeoSharPlusCPP/API/BridgeAPI.h"

#include <iostream>
#include <memory>

// GeoSharPlusCPP
#include "GSP_FB/cpp/mesh_generated.h"
#include "GSP_FB/cpp/pointArray_generated.h"
#include "GSP_FB/cpp/point_generated.h"
#include "GeoSharPlusCPP/Core/MathTypes.h"
#include "GeoSharPlusCPP/Serialization/GeoSerializer.h"
// cyCodeBase
#include "cyPointCloud.h"
#include "cySampleElim.h"
#include "cyVector.h"

namespace GS = GeoSharPlusCPP::Serialization;

extern "C" {

GEOSHARPLUS_API bool GEOSHARPLUS_CALL point3d_roundtrip(const uint8_t* inBuffer,
                                                        int inSize,
                                                        uint8_t** outBuffer,
                                                        int* outSize) {
  *outBuffer = nullptr;
  *outSize = 0;

  GeoSharPlusCPP::Vector3d pt;
  if (!GS::deserializePoint(inBuffer, inSize, pt)) {
    return false;
  }

  // Serialize the point into the allocated buffer
  if (!GS::serializePoint(pt, *outBuffer, *outSize)) {
    if (*outBuffer) delete[] *outBuffer;  // Cleanup
    *outBuffer = nullptr;
    *outSize = 0;

    return false;
  }

  return true;
}

GEOSHARPLUS_API bool GEOSHARPLUS_CALL point3d_array_roundtrip(
    const uint8_t* inBuffer, int inSize, uint8_t** outBuffer, int* outSize) {
  *outBuffer = nullptr;
  *outSize = 0;

  std::vector<GeoSharPlusCPP::Vector3d> points;
  if (!GS::deserializePointArray(inBuffer, inSize, points)) {
    return false;
  }

  // Serialize the point array into the allocated buffer
  if (!GS::serializePointArray(points, *outBuffer, *outSize)) {
    if (*outBuffer) delete[] *outBuffer;  // Cleanup
    *outBuffer = nullptr;
    *outSize = 0;

    return false;
  }

  return true;
}

GEOSHARPLUS_API bool GEOSHARPLUS_CALL BALpossionDiskElimSample(
    const uint8_t* inBuffer, int inSize, double generalArea, int dim, int n,
    uint8_t** outBuffer, int* outSize) {
  std::vector<GeoSharPlusCPP::Vector3d> points;
  if (!GS::deserializePointArray(inBuffer, inSize, points)) {
    return false;
  }

  // Serialize the point array into the allocated buffer
  if (!GS::serializePointArray(points, *outBuffer, *outSize)) {
    if (*outBuffer) delete[] *outBuffer;  // Cleanup
    *outBuffer = nullptr;
    *outSize = 0;

    return false;
  }

  // Convert GeoSharPlusCPP::Vector3d to cy::Vec3d
  std::vector<cy::Vec3d> inputPoints(points.size());
  for (const auto& point : points) {
    inputPoints.emplace_back(point.x(), point.y(), point.z());
  }

  // elimination to the given number of pts
  cy::WeightedSampleElimination<cy::Vec3d, float, 3> wse;
  std::vector<cy::Vec3d> outputPoints(n);

  //! Important!
  // d_max is used to define the sampling dist param based on sampling area
  // http://www.cemyuksel.com/cyCodeBase/soln/poisson_disk_sampling.html
  float d_max =
      2 * wse.GetMaxPoissonDiskRadius(dim, outputPoints.size(), generalArea);

  // 3D points, sampling in 2D plane
  wse.Eliminate(inputPoints.data(), inputPoints.size(), outputPoints.data(),
                outputPoints.size(), false, d_max, 2);

  // Convert outputPoints (cy::Vec3d) to std::vector<GeoSharPlusCPP::Vector3d>
  std::vector<GeoSharPlusCPP::Vector3d> sampledPoints(outputPoints.size());
  for (const auto& point : outputPoints) {
    sampledPoints.emplace_back(point.x, point.y, point.z);
  }
  // Serialize the sampled point array into the allocated buffer
  if (!GS::serializePointArray(sampledPoints, *outBuffer, *outSize)) {
    if (*outBuffer) delete[] *outBuffer;  // Cleanup
    *outBuffer = nullptr;
    *outSize = 0;
    return false;
  }

  return true;
}

}  // namespace GeoSharPlusCPP::Serialization