#pragma once
#include <cstdint>

#include "GeoSharPlusCPP/Core/Macro.h"

// BAL (BeingAliveLanguage) specific bridge functions

extern "C" {
// Poisson disk elimination sampling
GEOSHARPLUS_API bool GEOSHARPLUS_CALL BALpossionDiskElimSample(
    const uint8_t* inBuffer, int inSize, double generalArea, int dim, int n,
    uint8_t** outBuffer, int* outSize);
}
