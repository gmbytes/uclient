using UnityEngine;

namespace Client.Framework.Presentation
{
    public enum OutskirtsResourceKind { None = 0, Wood = 1, Stone = 2, Iron = 3, Food = 4 }

    public static class OutskirtsHex
    {
        public const int Columns = 5;
        public const int Rows = 9;
        public const int Count = Columns * Rows;
        public const int HomePosition = 20;
        public const int TerritoryRadius = 2;
        public const float Radius = .577350269f;
        public const float RowStep = .866025404f;

        public static Vector3 Center(int position)
        {
            // Keep legacy IDs; reserve the castle and its 12-cell land ring for the reference layout.
            int slot;
            switch (position)
            {
                case 20: slot = 23; break;
                case 23: slot = 20; break;
                case 13: slot = 3; break;
                case 3: slot = 13; break;
                case 16: slot = 1; break;
                case 1: slot = 16; break;
                case 21: slot = 45; break;
                case 45: slot = 21; break;
                case 34: slot = 9; break;
                case 9: slot = 34; break;
                default: slot = position; break;
            }
            int index = slot - 1;
            // Transpose the old 9-by-5 template without renumbering its cells.
            int row = index % Rows;
            return new Vector3(index / Rows + (row & 1) * .5f, 0, row * RowStep);
        }

        public static int Pick(Vector3 point)
        {
            for (int position = 1; position <= Count; position++)
            {
                var delta = point - Center(position);
                float x = Mathf.Abs(delta.x);
                if (x <= .5f && Mathf.Abs(delta.z) <= Radius - x * Radius)
                    return position;
            }
            return 0;
        }

        public static int Distance(int first, int second)
        {
            var a = Center(first);
            var b = Center(second);
            int ar = Mathf.RoundToInt(a.z / RowStep);
            int br = Mathf.RoundToInt(b.z / RowStep);
            int aq = Mathf.RoundToInt(a.x - (ar & 1) * .5f) - (ar - (ar & 1)) / 2;
            int bq = Mathf.RoundToInt(b.x - (br & 1) * .5f) - (br - (br & 1)) / 2;
            int q = aq - bq, r = ar - br;
            return (Mathf.Abs(q) + Mathf.Abs(r) + Mathf.Abs(q + r)) / 2;
        }
    }
}
