// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.proceduralterrain.md, section "Editor/TerraPatchImport.cs"
// ============================================================================
using System;
using UnityEngine;

namespace MidManStudio.ProceduralTerrain.EditorTools
{
    /// <summary>Makes a <see cref="TerraPatch"/> from a grey-scale picture.</summary>
    public static class TerraPatchImport
    {
        /// <summary>
        /// The pixels of a picture as a patch. Rows run from the bottom of the picture, which is the minimum z, so the top of
        /// the picture is north. The grey value is the weighted mean of red, green and blue as stored (no colour space
        /// conversion), 0 black to 1 white.
        /// </summary>
        public static TerraPatch FromColors(Color32[] pixels, int width, int height, string name,
            double minX, double minZ, double sizeX, double sizeZ, float minHeight, float maxHeight)
        {
            if (pixels == null)
            {
                throw new ArgumentNullException("pixels");
            }
            if (width < 2 || height < 2 || pixels.Length < width * height)
            {
                throw new ArgumentException("The picture needs at least 2 x 2 pixels, and as many colours as that.");
            }
            float[] data = new float[width * height];
            for (int i = 0; i < data.Length; i++)
            {
                Color32 c = pixels[i];
                data[i] = (c.r * 0.299f + c.g * 0.587f + c.b * 0.114f) / 255f;
            }
            TerraPatch patch = new TerraPatch();
            patch.name = name;
            patch.minX = minX;
            patch.minZ = minZ;
            patch.sizeX = sizeX;
            patch.sizeZ = sizeZ;
            patch.width = width;
            patch.height = height;
            patch.data = data;
            patch.minHeight = minHeight;
            patch.maxHeight = maxHeight;
            return patch;
        }

        /// <summary>The same from a texture, which must have Read/Write enabled in its import settings.</summary>
        public static TerraPatch FromTexture(Texture2D texture, string name,
            double minX, double minZ, double sizeX, double sizeZ, float minHeight, float maxHeight)
        {
            if (texture == null)
            {
                throw new ArgumentNullException("texture");
            }
            if (!texture.isReadable)
            {
                throw new InvalidOperationException("The texture is not readable. Turn on Read/Write in its import settings.");
            }
            return FromColors(texture.GetPixels32(), texture.width, texture.height, name, minX, minZ, sizeX, sizeZ, minHeight, maxHeight);
        }
    }
}
