namespace HereToSlay.View
{
    /// <summary>
    /// Sorting layers used to build the layered 2D scene (back to front).
    /// They are created automatically by the editor script HereToSlaySetup.
    /// Sorting orders are also globally increasing, so the scene still layers correctly if a layer is missing.
    /// </summary>
    public static class Layers
    {
        public const string Background = "Background"; // sky, moon, stars
        public const string Scenery = "Scenery";       // parallax mountains, forest, clouds
        public const string Table = "Table";           // wooden table, felt and zone mats
        public const string Board = "Board";           // cards in play: monsters, parties, piles
        public const string Hand = "Hand";             // the viewing player's hand
        public const string Focus = "Focus";           // hovered card, card being played

        public static readonly string[] All = { Background, Scenery, Table, Board, Hand, Focus };

        public const int BackgroundBase = -3000;
        public const int SceneryBase = -2000;
        public const int TableBase = -1000;
        public const int BoardBase = 0;
        public const int HandBase = 1000;
        public const int FocusBase = 2000;
    }
}
