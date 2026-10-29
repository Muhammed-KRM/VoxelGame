#ifndef STRATOCRAFT_CONTRACT_CRYPTO_H
#define STRATOCRAFT_CONTRACT_CRYPTO_H

#include "stratocraft_native_export.h"

#ifdef __cplusplus
extern "C" {
#endif

/// 32 baytlik kriptografik tohumdan (seed) 32 baytlik acik anahtar ve 64 baytlik gizli anahtar turetir.
STRATOCRAFT_API bool STRATOCRAFT_CALL StratocraftNative_Crypto_DeriveKeyPairEd25519(
    const uint8_t* seed32,
    uint8_t* outPublicKey32,
    uint8_t* outSecretKey64);

/// Kan Imzali Kontrat (EscrowVault) verisini sabit zamanli EdDSA (Ed25519) ile imzalar.
STRATOCRAFT_API bool STRATOCRAFT_CALL StratocraftNative_Crypto_SignContractEd25519(
    const uint8_t* payload,
    int32_t payloadLen,
    const uint8_t* secretKey64,
    uint8_t* outSignature64);

/// Kan Imzali Kontrat imzasini sabit zamanli dogrular.
STRATOCRAFT_API bool STRATOCRAFT_CALL StratocraftNative_Crypto_VerifyContractEd25519(
    const uint8_t* payload,
    int32_t payloadLen,
    const uint8_t* signature64,
    const uint8_t* publicKey32);

#ifdef __cplusplus
}
#endif

#endif // STRATOCRAFT_CONTRACT_CRYPTO_H
