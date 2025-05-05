#pragma once
#include <cstdint>

#include "GeoSharPlusCPP/Core/Macro.h"

extern "C" {
// Conduct a roundtrip serialization of a point3d
GEOSHARPLUS_API bool GEOSHARPLUS_CALL point3d_roundtrip(const uint8_t* InBuffer,
                                                        int inSize,
                                                        uint8_t** outBuffer,
                                                        int* outSize);
// Conduct a roundtrip serialization of a point array
GEOSHARPLUS_API bool GEOSHARPLUS_CALL point3d_array_roundtrip(
    const uint8_t* inBuffer, int inSize, uint8_t** outBuffer, int* outSize);

GEOSHARPLUS_API bool GEOSHARPLUS_CALL BALpossionDiskElimSample(
    const uint8_t* inBuffer, int inSize, double generalArea, int dim, int n,
    uint8_t** outBuffer, int* outSize);
}