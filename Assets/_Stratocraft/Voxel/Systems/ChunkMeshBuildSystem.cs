using System;
using System.Collections.Generic;
using Stratocraft.Core.Constants;
using Stratocraft.Voxel.Data;
using Stratocraft.Voxel.Jobs;

namespace Stratocraft.Voxel.Systems
{
    /// <summary>
    /// Kirlenen (DirtySubChunkMask != 0) Sub-Chunk'lar icin BinaryGreedyMeshingJob cizelgeleyen sistem.
    /// </summary>
    public sealed class ChunkMeshBuildSystem
    {
        private readonly GreedyVoxelQuad[] _scratchQuadBuffer = new GreedyVoxelQuad[16384];

        /// <summary>Son calistirmada uretilen toplam Quad sayisi.</summary>
        public int LastBuiltQuadCount { get; private set; }

        /// <summary>Son calistirmada guncellenen Sub-Chunk sayisi.</summary>
        public int LastRebuiltSubChunkCount { get; private set; }

        /// <summary>Aktif chunk'lardaki tum kirli Sub-Chunk'larin meshlerini gunceller.</summary>
        public int RebuildDirtySubChunks(IReadOnlyList<VoxelChunkComponent> chunks)
        {
            if (chunks == null)
            {
                throw new ArgumentNullException(nameof(chunks));
            }

            int totalQuads = 0;
            int rebuiltSubChunks = 0;
            Span<GreedyVoxelQuad> quadSpan = _scratchQuadBuffer.AsSpan();

            for (int i = 0; i < chunks.Count; i++)
            {
                VoxelChunkComponent chunk = chunks[i];
                ushort mask = chunk.DirtySubChunkMask;
                if (mask == 0)
                {
                    continue;
                }

                ReadOnlySpan<uint> voxels = chunk.Voxels.AsSpan();
                for (int subY = 0; subY < StratocraftConstants.SubChunksPerColumn; subY++)
                {
                    if (((mask >> subY) & 1) != 0)
                    {
                        totalQuads += BinaryGreedyMeshingJob.ExecuteSubChunk(voxels, subY, quadSpan);
                        rebuiltSubChunks++;
                    }
                }

                chunk.DirtySubChunkMask = 0;
            }

            LastBuiltQuadCount = totalQuads;
            LastRebuiltSubChunkCount = rebuiltSubChunks;
            return rebuiltSubChunks;
        }
    }
}
