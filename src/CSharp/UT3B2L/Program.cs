using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;

// This program uses a bitboard-like representation to represent the state of the game.
using Board = System.ValueTuple<ulong,ulong,ulong>;

using Move = System.Int32;

class UT3B2L
{
    // Weights for representing win, loss and draw outcomes.
    const int INFINITY = 1000000;
    public const int OUTCOME_WIN = INFINITY;
    const int OUTCOME_DRAW = 0;
    public const int OUTCOME_LOSS = -INFINITY;

    // Weights for line scoring.
    const int BIG_TWO_COUNT   = 90;
    const int BIG_ONE_COUNT   = 20;
    const int SMALL_TWO_COUNT = 8;
    const int SMALL_ONE_COUNT = 1;

    // Weights for positional scoring.
    const int CENTRE = 9;
    const int CORNER = 7;
    const int EDGE   = 5;
    const int SQ_BIG = 25;

    // Internal representation for *zone* value when the player can play in any zone.
    public const int ZONE_ANY = 9;

    // 81 is used to represent a "null move"
    // since values 0-80 are best used for move representation.
    public const Move NULL_MOVE = 81;

    // Masks for use in changing bitboards
    const ulong LINE        = 0b_111UL;
    const ulong CHUNK       = 0b_111_111_111UL;
    const ulong DBLCHUNK    = 0b_111_111_111_111_111_111UL;
    const ulong EXCLZONE    = 0b_111111_0000_111111111_111111111_111111111_111111111_111111111_111111111UL;
    const ulong CORNER_MASK = 0b_101_000_101UL;
    const ulong EDGE_MASK   = 0b_010_101_010UL;
    const ulong CENTRE_MASK = 0b_000_010_000UL;

    int[] EvalTableLarge = new int[262144];
    int[] EvalTableSmall = new int[262144];

    // Variable to contain the searching depth of the main AlphaBeta call.
    int SEARCHING_DEPTH;

    public void SetSearchingDepth(int depth)
    {
        SEARCHING_DEPTH = depth;
    }

    // Initialise PopCount lookup table by inserting bit count of each number
    // from 0 to 511 in the corresponding index in the array.
    public UT3B2L()
    {
        int[] PopCount = new int[512];

        int evalLarge, evalSmall, i;
        int evalPos;
        int usCount, themCount;
        ulong usLines, themLines;
        bool usWon, themWon;

        for (i = 0; i < 512; ++i)
            PopCount[i] = (i & 1) + ((i >> 1) & 1) + ((i >> 2) & 1) + ((i >> 3) & 1) +
                ((i >> 4) & 1) + ((i >> 5) & 1) + ((i >> 6) & 1) + ((i >> 7) & 1) + ((i >> 8) & 1);

        for (ulong us = 0UL; us < 512UL; ++us)
        {
            for (ulong them = 0UL; them < 512UL; ++them)
            {
                evalLarge = 0;
                evalSmall = 0;
                usLines = Lines(us);
                themLines = Lines(them);
                usWon = false;
                themWon = false;

                for (i = 0; i < 24; i += 3)
                {
                    usCount = PopCount[(usLines >> i) & LINE];
                    themCount = PopCount[(themLines >> i) & LINE];

                    if (usCount != 0 && themCount != 0)
                        continue;

                    if (usCount == 3)
                    {
                        usWon = true;
                        break;
                    }
                    if (themCount == 3)
                    {
                        themWon = true;
                        break;
                    }

                    evalLarge += usCount == 2 ? BIG_TWO_COUNT : usCount == 1 ? BIG_ONE_COUNT : 0;
                    evalLarge -= themCount == 2 ? BIG_TWO_COUNT : themCount == 1 ? BIG_ONE_COUNT : 0;

                    evalSmall += usCount == 2 ? SMALL_TWO_COUNT : usCount == 1 ? SMALL_ONE_COUNT : 0;
                    evalSmall -= themCount == 2 ? SMALL_TWO_COUNT : themCount == 1 ? SMALL_ONE_COUNT : 0;

                }

                evalPos = CORNER * (PopCount[us & CORNER_MASK] - PopCount[them & CORNER_MASK])
                        + EDGE   * (PopCount[us & EDGE_MASK]   - PopCount[them & EDGE_MASK])
                        + CENTRE * (PopCount[us & CENTRE_MASK] - PopCount[them & CENTRE_MASK]);

                if (usWon)
                    EvalTableLarge[(them << 9) | us] = OUTCOME_WIN;
                else if (themWon)
                    EvalTableLarge[(them << 9) | us] = OUTCOME_LOSS;
                else if (PopCount[us | them] == 9)
                    EvalTableLarge[(them << 9) | us] = OUTCOME_DRAW;
                else
                {
                    EvalTableLarge[(them << 9) | us] = evalLarge + evalPos * SQ_BIG;
                    EvalTableSmall[(them << 9) | us] = evalSmall + evalPos;
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ulong Lines(ulong grid)
    {
        // Returns an unsigned integer using LSH 24 bits in format:
        // 246 048 678 345 012 258 147 036
        // Each set of three bits represents the occupancies in a line in a 9-bit grid.
        // The 24 bits thus contains information on all 8 possible lines.
        return (
            ( grid       & 1) * 0b_000_100_000_000_100_000_000_100UL +
            ((grid >> 1) & 1) * 0b_000_000_000_000_010_000_100_000UL +
            ((grid >> 2) & 1) * 0b_100_000_000_000_001_100_000_000UL +
            ((grid >> 3) & 1) * 0b_000_000_000_100_000_000_000_010UL +
            ((grid >> 4) & 1) * 0b_010_010_000_010_000_000_010_000UL +
            ((grid >> 5) & 1) * 0b_000_000_000_001_000_010_000_000UL +
            ((grid >> 6) & 1) * 0b_001_000_100_000_000_000_000_001UL +
            ((grid >> 7) & 1) * 0b_000_000_010_000_000_000_001_000UL +
            ((grid >> 8) & 1) * 0b_000_001_001_000_000_001_000_000UL
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    bool LinePresence(ulong grid)
    {
        return 0 != (
            (0b10110110L | ((grid & 1) * 0xffL)) &
            (0b11101110L | (((grid >> 1) & 1) * 0xffL)) &
            (0b01011110L | (((grid >> 2) & 1) * 0xffL)) &
            (0b11110101L | (((grid >> 3) & 1) * 0xffL)) &
            (0b00101101L | (((grid >> 4) & 1) * 0xffL)) &
            (0b11011101L | (((grid >> 5) & 1) * 0xffL)) &
            (0b01110011L | (((grid >> 6) & 1) * 0xffL)) &
            (0b11101011L | (((grid >> 7) & 1) * 0xffL)) &
            (0b10011011L | (((grid >> 8) & 1) * 0xffL))
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    int ToggleShift(bool side, int num) {
        return side ? num : 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    int ToggleEval(bool side, int num) {
        return side ? -num : num;
    }

    // The functions in this program all assume that we are starting with a valid board position.
    // Only valid positions will be reached if the program only ever uses its own functions to
    // play moves on the boards.

    public List<int> GenerateMoves(Board board)
    {
        (ulong us, ulong them, ulong share) = board;

        if (LinePresence(share >> 36) || LinePresence(share >> 45))
            return new List<int>();

        int zone = (int)((share >> 54) & 0b1111);

        List<int> moveList = new List<int>();

        switch (zone)
        {
            case ZONE_ANY:
            {
                ulong nwToSw = us | them;
                ulong sToSe = (share >> 18) | share;
                ulong large = (share >> 36) | (share >> 45);

                for (int i = 0; i < 63; ++i)
                    if (((nwToSw >> i) & 1) == 0 && ((large >> (i/9)) & 1) == 0)
                        moveList.Add(i);
                for (int i = 63; i < 81; ++i)
                    if (((sToSe >> (i - 63)) & 1) == 0 && ((large >> (i/9)) & 1) == 0)
                        moveList.Add(i);
                break;
            }
            case 7: case 8:
            {
                zone *= 9;
                ulong sToSe = ((share >> 18) | share) >> (zone - 63);
                for (int i = 0; i < 9; ++i)
                    if (((sToSe >> i) & 1) == 0)
                        moveList.Add(zone + i);
                break;
            }
            default:
            {
                zone *= 9;
                ulong nwToSw = (us | them) >> zone;
                for (int i = 0; i < 9; ++i)
                    if (((nwToSw >> i) & 1) == 0)
                        moveList.Add(zone + i);
                break;
            }
        }

        return moveList;
    }

    // Returns the new board state after the given move has been played by the given side.
    // This will not affect the value of the board passed into this function.
    // side = false if player is X, true if player is O
    public Board PlayMove(Board board, int move, bool side)
    {
        (ulong us, ulong them, ulong share) = board;

        bool lineOccupancy;
        ulong nextChunk;

        if (move > 62)
        {
            // The relevant position is the square number - 63, then 18 places further if O is playing.
            share |= 1UL << (move - 63 + ToggleShift(side, 18));
            // Relevant zone is INT(move / 9), so we bitshift by `9*zone - 63` to get correct zone,
            // once again offsetting by 18 if player O is playing.
            lineOccupancy = LinePresence((share >> (9*(move / 9) - 63 + ToggleShift(side, 18))) & CHUNK);
        }
        else if (!side) // Player X is playing.
        {
            us |= 1UL << move;
            lineOccupancy = LinePresence((us >> (9*(move / 9))) & CHUNK);
        }
        else // Player O is playing.
        {
            them |= 1UL << move;
            lineOccupancy = LinePresence((them >> (9*(move / 9))) & CHUNK);
        }

        if (lineOccupancy)
            share |= 1UL << (36 + ToggleShift(side, 9) + move / 9);

        // To locate the occupancies for the zone corresponding to the next move,
        // whether the zone will be S to SE or otherwise needs to be considered,
        // due to the different locations of these zones in the Board representation.
        nextChunk = move % 9 > 6 ?
            ((share | (share >> 18)) >> (9*((move % 9) - 7))) & CHUNK : // Access `share` if next zone is S or SE.
            ((us | them) >> (9*(move % 9))) & CHUNK; // Access `us` and `them` otherwise.

        // Normally, the zone of the next move is determined by move % 9.
        // However, if that corresponding zone is completely filled, or
        // the large occupancy corresponding to the zone is occupied, then the player can move anywhere.
        int zone = nextChunk == CHUNK || (((share | (share >> 9)) >> (36 + move % 9)) & 1) == 1 ?
            ZONE_ANY :
            move % 9;

        // Return board with new updated values, putting `zone` value into the correct place in `share`.
        return (us, them, (share & EXCLZONE) | ((ulong)zone << 54));
    }

    // Heuristic for evaluating a particular board from the perspective of a particular side,
    // once the minimax reaches a depth = 0 or other leaf node.
    int Evaluate(Board board, bool side)
    {
        (ulong us, ulong them, ulong share) = board;

        // Throughout the function, the evaluation is calculated from the perspective of X.
        // It is then flipped according to *side* at the end.

        // Line scoring begins here.

        int eval = EvalTableLarge[(share >> 36) & DBLCHUNK];

        if (eval == OUTCOME_WIN || eval == OUTCOME_LOSS)
            return ToggleEval(side, eval);

        ulong usData, themData;
        ulong large = ((share >> 36) | (share >> 45)) & CHUNK;

        if (large == CHUNK)
            return OUTCOME_DRAW;

        // Now, we loop through each of the 9 zones.
        for (int i = 0; i < 9; ++i)
        {
            // To avoid code duplication, we check for whether the zone is from NW to SW or S to SE,
            // as the representation for each requires separate handling.
            if (i < 7)
            {
                usData = (us >> (9*i)) & CHUNK;     // Stores X occupancies of the current zone
                themData = (them >> (9*i)) & CHUNK; // Stores O occupancies of the current zone
            }
            else
            {
                usData = (share >> (9*i - 63)) & CHUNK;   // Stores X occupancies of the current zone
                themData = (share >> (9*i - 45)) & CHUNK; // Stores O occupancies of the current zone
            }

            // If the zone is completely filled or the corresponding spot in the large grid is filled,
            // we do not score this zone.
            if ((usData | themData) == CHUNK || ((large >> i) & 1) == 1)
                continue;

            eval += EvalTableSmall[(themData << 9) | usData];
        }
        return ToggleEval(side, eval);
    }

    // The main alpha-beta minimax function. Returns evaluation and principal variation.
    public ValueTuple<int,Move[]> AlphaBeta(Board board, bool side, int depth, int alpha, int beta)
    {
        // Reached leaf node, so return static evaluation and empty PV.
        if (depth == 0)
        {
            int adjustedEval = Evaluate(board, side);
            if (adjustedEval == OUTCOME_WIN)
                adjustedEval -= SEARCHING_DEPTH - depth;
            else if (adjustedEval == OUTCOME_LOSS)
                adjustedEval += SEARCHING_DEPTH - depth;
            return (adjustedEval, new int[SEARCHING_DEPTH]);
        }

        List<int> moveList = GenerateMoves(board); // Find all legal moves.
        int eval;

        if (moveList.Count() == 0) // If there are no legal moves, the game is over.
        {
            eval = ToggleEval(side, EvalTableLarge[(board.Item3 >> 36) & DBLCHUNK]);
            if (eval == OUTCOME_WIN)
                eval -= SEARCHING_DEPTH - depth;
            else if (eval == OUTCOME_LOSS)
                eval += SEARCHING_DEPTH - depth;
            else
                eval = OUTCOME_DRAW;
            return (eval, new int[SEARCHING_DEPTH - depth]); // Reached leaf node, so return empty PV.
        }

        int length = moveList.Count();
        int[] pv = new int[SEARCHING_DEPTH], line;

        // Iterate over all legal moves.
        for (int i = 0; i < length; ++i)
        {
            // Recursive minimax call, using negamax construct as evaluation is symmetric.
            (eval, line) = AlphaBeta(PlayMove(board, moveList[i], side), !side, depth-1, -beta, -alpha);
            // So, we take the negative of the evaluation.
            eval = -eval;

            line[SEARCHING_DEPTH - depth] = moveList[i]; // Record this move in the line.

            if (eval >= beta) // Fail hard beta-cutoff
                return (beta, line);
            else if (eval > alpha) // New best move found
            {
                alpha = eval;
                pv = line;    // Update PV.
            }
        }
        return (alpha, pv); // Return evaluation of best option, and the matching PV.
    }

    public void PrintBoard(Board board)
    {
        string[] sqArr = {"NW", "N", "NE", "W", "C", "E", "SW", "S", "SE"};

        (ulong us, ulong them, ulong share) = board;
        int US = 1, THEM = -1;

        // Make arrays to contain the values of each square
        int[][] small = new int[][] {
            new int[] {0, 0, 0, 0, 0, 0, 0, 0, 0},
            new int[] {0, 0, 0, 0, 0, 0, 0, 0, 0},
            new int[] {0, 0, 0, 0, 0, 0, 0, 0, 0},
            new int[] {0, 0, 0, 0, 0, 0, 0, 0, 0},
            new int[] {0, 0, 0, 0, 0, 0, 0, 0, 0},
            new int[] {0, 0, 0, 0, 0, 0, 0, 0, 0},
            new int[] {0, 0, 0, 0, 0, 0, 0, 0, 0},
            new int[] {0, 0, 0, 0, 0, 0, 0, 0, 0},
            new int[] {0, 0, 0, 0, 0, 0, 0, 0, 0}
        };
        int[] large = new int[] {0, 0, 0, 0, 0, 0, 0, 0, 0};
        int zone = (int)((share >> 54) & 0b1111);

        // Fill in each element of the arrays with the correct values
        for (int i = 0; i < 63; ++i)
        {
            if (((us >> i) & 1) == 1)
                small[i / 9][i % 9] = US;
            else if (((them >> i) & 1) == 1)
                small[i / 9][i % 9] = THEM;
        }
        for (int i = 0; i < 18; ++i)
        {
            if (((share >> i) & 1) == 1)
                small[i / 9 + 7][i % 9] = US;
            else if (((share >> (i + 18)) & 1) == 1)
                small[i / 9 + 7][i % 9] = THEM;
        }
        for (int i = 0; i < 9; ++i)
        {
            if (((share >> (i + 36)) & 1) == 1)
                large[i] = US;
            else if (((share >> (i + 45)) & 1) == 1)
                large[i] = THEM;
        }

        ArraySegment<int[]> bigRow;
        Console.WriteLine("---+---+---");
        for (int i = 0; i < 81; i += 27)
        {
            // Take the corresponding horizontal row of the large grid
            bigRow = new ArraySegment<int[]>(small, i/9, 3);

            // The next three rows in output come from this large grid row
            for (int j = 0; j < 9; j += 3)
            {
                // Select top row, middle row, then bottom row from each of the grids
                Console.WriteLine(string.Join(
                    "|",
                    (
                        from grid in bigRow
                        select string.Join(
                            "",
                            (
                                from smallRow in
                                new ArraySegment<int>(grid, j, 3)
                                select smallRow == US ? "X" : smallRow == THEM ? "O" : "."
                            )
                        )
                    )
                ));
            }
            Console.WriteLine("---+---+---");
        }
        // Print the large grid contents in groups of three, starting from nw-n-ne row to sw-s-se row
        for (int i = 0; i < 3; ++i)
            Console.WriteLine(string.Join("",
                (from square in new ArraySegment<int>(large, 3*i, 3)
                    select square == US ? "X" : square == THEM ? "O" : ".")));
        Console.WriteLine("ZONE: " + (zone == ZONE_ANY ? "ANY" : sqArr[zone]));
    }

    public string BoardString(Board board)
    {
        string[] sqArr = {"nw", "n", "ne", "w", "c", "e", "sw", "s", "se"};

        (ulong us, ulong them, ulong share) = board;
        int zone = (int)((share >> 54) & 0b1111);
        List<List<int>> cells = new List<List<int>>();
        for (int i = 0; i < 81; i += 27)
            for (int j = 0; j < 9; j += 3) {
                List<int> row = new List<int>();
                for (int k = 0; k < 27; k += 9)
                    row.AddRange(new int[] {i+j+k, i+j+k+1, i+j+k+2});
                cells.Add(row);
            }

        string cellString = string.Join(
            "/",
            (
                from v in cells
                select string.Join(
                    "",
                    (
                        from i in v
                        select
                        i > 62 ?
                        (
                            ((share >> (i-63)) & 1) == 1 ? "x" :
                            ((share >> (i-45)) & 1) == 1 ? "o" : "."
                        ) :
                        (
                            ((us >> i) & 1) == 1 ? "x" :
                            ((them >> i) & 1) == 1 ? "o" : "."
                        )
                    )
                )
            )
        )
            .Replace(".........", "9")
            .Replace("........", "8")
            .Replace(".......", "7")
            .Replace("......", "6")
            .Replace(".....", "5")
            .Replace("....", "4")
            .Replace("...", "3")
            .Replace("..", "2")
            .Replace(".", "1");

        return cellString + " " + (zone == ZONE_ANY ? "any" : sqArr[zone]);
    }

    public Board? BoardFromString(string boardString)
    {
        (ulong us, ulong them, ulong share) = (0UL, 0UL, 0UL);
        string decompressedString = boardString
            .Replace("1", ".")
            .Replace("2", "..")
            .Replace("3", "...")
            .Replace("4", "....")
            .Replace("5", ".....")
            .Replace("6", "......")
            .Replace("7", ".......")
            .Replace("8", "........")
            .Replace("9", ".........");
        string[] cellAndZone = decompressedString.Split();
        if (cellAndZone.Length != 2)
            return null;
        string[] sqArr = {"nw", "n", "ne", "w", "c", "e", "sw", "s", "se"};
        string cell = cellAndZone[0];
        string zone = cellAndZone[1];
        if (Array.IndexOf(sqArr, zone) != -1)
        {
            share |= (ulong)(Array.IndexOf(sqArr, zone)) << 54;
        }
        else if (zone == "any")
        {
            share |= (ulong)ZONE_ANY << 54;
        }
        else
        {
            return null;
        }
        string[] rows = cell.Split('/');
        if (rows.Length != 9)
        {
            return null;
        }
        foreach (string row in rows)
        {
            if (row.Length != 9)
                return null;
        }
        List<int> rowIndices = new List<int>();
        for (int i = 0; i < 81; i += 27)
            for (int j = 0; j < 9; j += 3)
                for (int k = 0; k < 27; k += 9)
                    rowIndices.AddRange(new int[] {i+j+k, i+j+k+1, i+j+k+2});
        decompressedString = cell.Replace("/", "");
        for (int j = 0; j < decompressedString.Length; ++j)
        {
            char c = decompressedString[j];
            int i = rowIndices[j];
            if (i > 62)
            {
                if (c == 'x')
                    share |= 1UL << (i - 63);
                else if (c == 'o')
                    share |= 1UL << (i - 45);
            }
            else if (c == 'x')
            {
                us |= 1UL << i;
            }
            else if (c == 'o')
            {
                them |= 1UL << i;
            }
        }
        ulong firstSevenUs = us;
        ulong firstSevenThem = them;
        for (int i = 0; i < 7; ++i)
        {
            if (LinePresence(firstSevenUs >> (9 * i)))
                share |= 1UL << (36 + i);
            else if (LinePresence(firstSevenThem >> (9 * i)))
                share |= 1UL << (45 + i);
        }
        ulong lastTwoUs = share;
        ulong lastTwoThem = share >> 18;
        for (int i = 7; i < 9; ++i)
        {
            if (LinePresence(lastTwoUs >> (9*i - 63)))
                share |= 1UL << (36 + i);
            else if (LinePresence(lastTwoThem >> (9*i - 63)))
                share |= 1UL << (45 + i);
        }
        return (us, them, share);
    }

    // First part is the zone, second part is the spot within that zone's grid.
    public string MoveString(int move)
    {
        string[] sqArr = {"nw", "n", "ne", "w", "c", "e", "sw", "s", "se"};
        return sqArr[move / 9] + "/" + sqArr[move % 9];
    }

    public Move MoveFromString(string moveString)
    {
        string[] zoneAndSquare = moveString.Split('/');
        if (zoneAndSquare.Length != 2)
        {
            return NULL_MOVE;
        }
        string[] sqArr = {"nw", "n", "ne", "w", "c", "e", "sw", "s", "se"};
        int zone = Array.IndexOf(sqArr, zoneAndSquare[0]);
        int square = Array.IndexOf(sqArr, zoneAndSquare[1]);
        return zone != -1 && square != -1 ? 9 * zone + square : NULL_MOVE;
    }

    // The evaluation score is always outputted in the following format:
    // The first character is one of the five:
    // "+": Meaning the position is winning for the AI, and the following number is the score
    // "-": Meaning the position is losing for the AI, and the following number is the score
    // "W": Meaning the AI has found a forced win, and the following number is the number of moves in which it will happen
    // "L": Meaning the AI has found the position to be a forced loss, and the following number is the number of moves in which it will happen
    // "D": Meaning the AI has found a draw, and the following number is always 0.
    public string EvalString(int eval)
    {
        return (eval <= OUTCOME_LOSS + SEARCHING_DEPTH) ? "L" + (eval - OUTCOME_LOSS) :
            (eval >= OUTCOME_WIN - SEARCHING_DEPTH) ? "W" + (OUTCOME_WIN - eval) :
            (eval == OUTCOME_DRAW) ? "D0" :
            (eval > 0 ? "+" : "-") + Math.Abs(eval).ToString();
    }
}

class Program
{
    public static void Main(string[] args)
    {
        UT3B2L ut3b2l = new UT3B2L();

        Console.WriteLine("ready");

        List<(Board,Move)> history = new List<(Board,Move)>();

        Board startBoard = (
            0b0_000000000_000000000_000000000_000000000_000000000_000000000_000000000UL,
            0b0_000000000_000000000_000000000_000000000_000000000_000000000_000000000UL,
            0b000000_1001_000000000_000000000_000000000_000000000_000000000_000000000UL
        );
        history.Add((startBoard, UT3B2L.NULL_MOVE));

        Stopwatch timer = new Stopwatch();

        bool runMainLoop = true;

        while (runMainLoop)
        {
            string commandString = Console.ReadLine() ?? "";
            string[] command = commandString.Split();

            if (command.Length == 0)
            {
                continue;
            }

            bool currentPlayer;

            switch (command[0])
            {
                case "newgame":
                    if (command.Length < 3)
                    {
                        Console.WriteLine("newgame invalid args");
                        continue;
                    }
                    Board? newStartingBoard = ut3b2l.BoardFromString(command[1] + " " + command[2]);
                    if (newStartingBoard != null)
                    {
                        history.Clear();
                        history.Add((newStartingBoard.Value, UT3B2L.NULL_MOVE));
                        Console.WriteLine("newgame ok");
                    }
                    else
                    {
                        Console.WriteLine("newgame invalid pos");
                    }
                    break;
                case "go":
                    if (command.Length < 2)
                    {
                        Console.WriteLine("info error no depth");
                        continue;
                    }
                    currentPlayer = (history.Count & 1) == 0;
                    int depth;
                    if (int.TryParse(command[1], out depth))
                    {
                        if (depth <= 0)
                        {
                            Console.WriteLine("info error invalid depth");
                            continue;
                        }
                        // There is no limit other than negative of zero depth here.
                        Board board = history.Last().Item1;
                        ut3b2l.SetSearchingDepth(depth);
                        timer.Reset();
                        timer.Start();
                        (int eval, Move[] line) = ut3b2l.AlphaBeta(
                            board,
                            currentPlayer,
                            depth,
                            UT3B2L.OUTCOME_LOSS,
                            UT3B2L.OUTCOME_WIN
                        );
                        timer.Stop();
                        long duration = timer.ElapsedMilliseconds;
                        Console.WriteLine(
                            "info depth " + depth +
                            " pv " +
                            string.Join(" ", (from m in line select ut3b2l.MoveString(m))) +
                            " eval " + ut3b2l.EvalString(eval) +
                            " time " + duration
                        );
                        if (line.Length != 0)
                            history.Add((ut3b2l.PlayMove(board, line[0], currentPlayer), line[0]));
                    }
                    else
                    {
                        Console.WriteLine("info error invalid depth");
                    }
                    break;
                case "play":
                    if (command.Length != 2)
                    {
                        Console.WriteLine("move invalid");
                        continue;
                    }
                    if (command[1] == "null")
                    {
                        (Board lastBoard, Move lastMove) = history.Last();
                        history.Add((lastBoard, lastMove));
                        Console.WriteLine("move pos " + ut3b2l.BoardString(lastBoard));
                        continue;
                    }
                    Move move = ut3b2l.MoveFromString(command[1]);
                    if (move != UT3B2L.NULL_MOVE)
                    {
                        Board board = history.Last().Item1;
                        if (ut3b2l.GenerateMoves(board).Contains(move))
                        {
                            currentPlayer = (history.Count & 1) == 0;
                            Board newBoard = ut3b2l.PlayMove(board, move, currentPlayer);
                            history.Add((newBoard, move));
                            Console.WriteLine("move pos " + ut3b2l.BoardString(newBoard));
                        }
                        else
                        {
                            Console.WriteLine("move illegal");
                        }
                    }
                    else
                    {
                        Console.WriteLine("move invalid");
                    }
                    break;
                case "undo":
                    (Board,Move)? item = history.LastOrDefault();
                    if (item != null)
                    {
                        if (item == (startBoard, UT3B2L.NULL_MOVE))
                        {
                            Console.WriteLine("undo stackempty");
                        }
                        else
                        {
                            history.RemoveAt(history.Count - 1);
                            Console.WriteLine("undo ok");
                        }
                    }
                    else
                    {
                        Console.WriteLine("undo stackempty");
                    }
                    break;
                case "gamepos":
                    Console.WriteLine(ut3b2l.BoardString(history.Last().Item1));
                    break;
                case "d":
                    ut3b2l.PrintBoard(history.Last().Item1);
                    break;
                case "q":
                    runMainLoop = false;
                    break;
                default:
                    Console.WriteLine("badkeyword");
                    break;
            }
        }
    }
}
