#ifndef STRATOCRAFT_NATIVE_EXPORT_H
#define STRATOCRAFT_NATIVE_EXPORT_H

#include <stdint.h>
#include <stdbool.h>

#if defined(_WIN32) || defined(_WIN64)
    #if defined(STRATOCRAFT_NATIVE_EXPORTS)
        #define STRATOCRAFT_API __declspec(dllexport)
    #else
        #define STRATOCRAFT_API __declspec(dllimport)
    #endif
    #define STRATOCRAFT_CALL __cdecl
#else
    #define STRATOCRAFT_API __attribute__((visibility("default")))
    #define STRATOCRAFT_CALL
#endif

#ifdef __cplusplus
extern "C" {
#endif

/// CPU SIMD yetenek sorgusu (0 = Scalar, 1 = SSE4.2, 2 = AVX2, 3 = AVX-512)
STRATOCRAFT_API int32_t STRATOCRAFT_CALL StratocraftNative_GetSimdCapabilities(void);

/// StratocraftNative ABI surum numarasi (Faz 0 = 100)
STRATOCRAFT_API int32_t STRATOCRAFT_CALL StratocraftNative_GetAbiVersion(void);

#ifdef __cplusplus
}
#endif

#endif // STRATOCRAFT_NATIVE_EXPORT_H
