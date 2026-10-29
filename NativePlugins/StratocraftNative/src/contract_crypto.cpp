#include "contract_crypto.h"

#include <cstring>
#include <monocypher.h>
#include <monocypher-ed25519.h>

extern "C"
{

STRATOCRAFT_API bool STRATOCRAFT_CALL StratocraftNative_Crypto_DeriveKeyPairEd25519(
    const uint8_t* seed32,
    uint8_t* outPublicKey32,
    uint8_t* outSecretKey64)
{
    if (seed32 == nullptr || outPublicKey32 == nullptr || outSecretKey64 == nullptr)
    {
        return false;
    }

    uint8_t seedCopy[32];
    std::memcpy(seedCopy, seed32, 32);
    crypto_ed25519_key_pair(outSecretKey64, outPublicKey32, seedCopy);
    crypto_wipe(seedCopy, sizeof(seedCopy));
    return true;
}

STRATOCRAFT_API bool STRATOCRAFT_CALL StratocraftNative_Crypto_SignContractEd25519(
    const uint8_t* payload,
    int32_t payloadLen,
    const uint8_t* secretKey64,
    uint8_t* outSignature64)
{
    if (payload == nullptr || payloadLen <= 0 || secretKey64 == nullptr || outSignature64 == nullptr)
    {
        return false;
    }

    crypto_ed25519_sign(
        outSignature64,
        secretKey64,
        payload,
        static_cast<size_t>(payloadLen));
    return true;
}

STRATOCRAFT_API bool STRATOCRAFT_CALL StratocraftNative_Crypto_VerifyContractEd25519(
    const uint8_t* payload,
    int32_t payloadLen,
    const uint8_t* signature64,
    const uint8_t* publicKey32)
{
    if (payload == nullptr || payloadLen <= 0 || signature64 == nullptr || publicKey32 == nullptr)
    {
        return false;
    }

    // crypto_ed25519_check: gecerli imzada 0, sahte/bozuk imzada -1 dondurur (sabit zamanli)
    const int status = crypto_ed25519_check(
        signature64,
        publicKey32,
        payload,
        static_cast<size_t>(payloadLen));
    return status == 0;
}

} // extern "C"
