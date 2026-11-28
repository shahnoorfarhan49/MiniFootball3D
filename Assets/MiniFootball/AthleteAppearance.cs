using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace MiniFootball
{
    [DisallowMultipleComponent]
    public sealed class AthleteAppearance : MonoBehaviour
    {
        // Assigned on the generated prefab. Both bodies use the same rig and material.
        public SkinnedMeshRenderer Near, Far;
        public SkinnedMeshRenderer[] Hair;
        Texture2D kit;
        MaterialPropertyBlock overrides;

        const int Width = 256, Height = 128;
        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        static readonly Color32[] Complexions = {
            new Color32(238,190,156,255), new Color32(210,154,120,255), new Color32(186,129,92,255),
            new Color32(150,99,71,255), new Color32(115,74,54,255), new Color32(88,58,46,255)
        };
        static readonly Color32[] HairTones = {
            new Color32(42,33,28,255), new Color32(91,61,42,255), new Color32(48,37,35,255),
            new Color32(131,95,62,255), new Color32(35,29,29,255)
        };

        public static void Apply(GameObject actor, int id, string nameOnShirt, int number, Color primary, Color trim)
        {
            var appearance = actor.GetComponent<AthleteAppearance>();
            appearance.Dress(id, nameOnShirt, number, primary, trim);
        }

        void Dress(int id, string nameOnShirt, int number, Color primary, Color trim)
        {
            // Stature and shoulder width vary independently while preserving the bind matrices.
            float stature = .945f + (id * 7 % 19) * .006f;
            float build = .95f + (id * 11 % 17) * .006f;
            transform.localScale = new Vector3(build, stature, build);

            if (!kit) kit = NewAtlas();
            FillAtlas(kit, id, nameOnShirt, number, primary, trim);
            overrides ??= new MaterialPropertyBlock();
            overrides.SetTexture(BaseMap, kit);

            SetBody(Near, id);
            SetBody(Far, id);
            int style = (id * 7 + id / 4) % Hair.Length;
            for (int i = 0; i < Hair.Length; i++)
            {
                Hair[i].gameObject.SetActive(i == style);
                if (i == style) Hair[i].SetPropertyBlock(overrides);
            }
        }

        void SetBody(SkinnedMeshRenderer skin, int id)
        {
            skin.SetPropertyBlock(overrides);
            Mesh mesh = skin.sharedMesh;
            for (int index = 0; index < mesh.blendShapeCount; index++)
            {
                // Some FBX importer versions prefix shape names with the originating mesh.
                string shape = mesh.GetBlendShapeName(index);
                if (shape.EndsWith("BroadJaw", StringComparison.Ordinal))
                    skin.SetBlendShapeWeight(index, (id * 37) % 101);
                else if (shape.EndsWith("Cheekbones", StringComparison.Ordinal))
                    skin.SetBlendShapeWeight(index, (17 + id * 43) % 101);
            }
        }

        void OnDestroy()
        {
            if (kit) Destroy(kit);
        }

        public static Texture2D NewAtlas() => new Texture2D(Width, Height, TextureFormat.RGBA32, true)
        {
            name = "Football kit atlas", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 2
        };

        public static void FillAtlas(Texture2D texture, int id, string nameOnShirt, int number, Color primary, Color trim)
        {
            Color skin = Complexions[(id * 5 + id / 7) % Complexions.Length];
            Color hair = HairTones[(id * 3 + id / 5) % HairTones.Length];
            Color32 white = new Color32(236,234,225,255), dark = new Color32(35,37,44,255);
            Color32[] tiles = {
                skin, primary, trim, primary, dark, hair, white, new Color32(66,52,41,255),
                Color.Lerp(skin,new Color32(128,53,56,255),.46f), trim, Color.Lerp(hair,skin,.10f),
                new Color32(111,116,116,255), Color.Lerp(primary,trim,.4f), Color.Lerp(skin,dark,.23f), white, trim
            };
            var pixels = new Color32[Width * Height];
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                pixels[y * Width + x] = y < Height / 2 ? tiles[x / 32 + y / 32 * 8] : (Color32)primary;
            }

            Color ink = primary.grayscale > .56f ? (Color)dark : white;
            DrawText(pixels, ShirtName(nameOnShirt), 114, 2, ink);
            DrawText(pixels, number.ToString(CultureInfo.InvariantCulture), 70, 5, ink);
            texture.SetPixels32(pixels);
            texture.Apply(true, false);
        }

        static string ShirtName(string name)
        {
            // The atlas carries an uppercase surname while the UI retains the complete Unicode name.
            string[] parts = name.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "";
            var result = new StringBuilder();
            string surname = parts[parts.Length - 1].ToUpperInvariant().Replace("Ø", "O").Normalize(NormalizationForm.FormD);
            foreach (char ch in surname)
            {
                if (ch >= 'A' && ch <= 'Z') result.Append(ch);
                if (result.Length == 18) break;
            }
            return result.ToString();
        }

        // Five columns per glyph, low bit at the top. The tiny type is painted directly
        // into the kit atlas so it follows the skinned fabric at both LODs.
        static readonly byte[,] Font = {
            {126,17,17,17,126},{127,73,73,73,54},{62,65,65,65,34},{127,65,65,34,28},
            {127,73,73,73,65},{127,9,9,9,1},{62,65,73,73,122},{127,8,8,8,127},
            {0,65,127,65,0},{32,64,65,63,1},{127,8,20,34,65},{127,64,64,64,64},
            {127,2,12,2,127},{127,4,8,16,127},{62,65,65,65,62},{127,9,9,9,6},
            {62,65,81,33,94},{127,9,25,41,70},{70,73,73,73,49},{1,1,127,1,1},
            {63,64,64,64,63},{31,32,64,32,31},{127,32,24,32,127},{99,20,8,20,99},
            {3,4,120,4,3},{97,81,73,69,67},
            {62,81,73,69,62},{0,66,127,64,0},{66,97,81,73,70},{33,65,69,75,49},
            {24,20,18,127,16},{39,69,69,69,57},{60,74,73,73,48},{1,113,9,5,3},
            {54,73,73,73,54},{6,73,73,41,30}
        };

        static void DrawText(Color32[] pixels, string text, int bottom, int scale, Color32 ink)
        {
            int left = (Width - (text.Length * 6 - 1) * scale) / 2;
            foreach (char ch in text)
            {
                int glyph = ch >= '0' && ch <= '9' ? 26 + ch - '0' : ch - 'A';
                if (glyph < 0 || glyph >= Font.GetLength(0)) continue;
                for (int column = 0; column < 5; column++)
                for (int row = 0; row < 7; row++)
                {
                    if ((Font[glyph, column] & (1 << row)) == 0) continue;
                    for (int dy = 0; dy < scale; dy++)
                    for (int dx = 0; dx < scale; dx++)
                        pixels[(bottom + (6 - row) * scale + dy) * Width + left + column * scale + dx] = ink;
                }
                left += 6 * scale;
            }
        }
    }
}
