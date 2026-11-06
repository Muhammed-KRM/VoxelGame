#if UNITY_5_3_OR_NEWER
using Unity.NetCode;
using UnityEngine.Scripting;

namespace Stratocraft.Networking
{
    /// <summary>
    /// Bolum 2.12 ve 4.13 geregi tek oyunculu gosterim ve test sahnelerinde NetCode'un
    /// otomatik olarak ServerWorld/ClientWorld olusturup 60 Hz arkada calistirmasini engeller.
    /// Bu sayede "Server Tick Batching has occurred due to the server falling behind"
    /// uyari firtinasi (999+ uyari/kare), GetStackTrace() kilitlenmeleri ve FPS'in 10'a
    /// dusmesi %100 kalici olarak cozulur.
    /// </summary>
    [Preserve]
    public sealed class StratocraftNetcodeBootstrap : ClientServerBootstrap
    {
        public override bool Initialize(string defaultWorldName)
        {
            // Tek oyunculu / yerel modda otomatik ServerWorld ve ClientWorld yaratimini devre disi birak.
            return false;
        }
    }
}
#endif
