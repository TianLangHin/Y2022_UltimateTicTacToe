import java.util.ArrayList;
import java.util.Arrays;
import java.util.Scanner;

// Record for storing a pair containing an evaluation and the variation.
record LineScorePair(int Score, int[] Line) {}

// Record for storing the state of a Board, using a bitboard-like representation internally.
// All three pieces of data do not use the bit reserved for the negative in 2's complement.
record Board(long Us, long Them, long Share) {}

class UT3B2
{
    // Weights for representing win, loss and draw outcomes.
    final int INFINITY = 1000000;
    final int OUTCOME_WIN = INFINITY;
    final int OUTCOME_DRAW = 0;
    final int OUTCOME_LOSS = -INFINITY;

    // Weights for line scoring.
    final int BIG_TWO_COUNT   = 90;
    final int BIG_ONE_COUNT   = 20;
    final int SMALL_TWO_COUNT = 8;
    final int SMALL_ONE_COUNT = 1;

    // Weights for positional scoring.
    final int CENTRE = 9;
    final int CORNER = 7;
    final int EDGE   = 5;
    final int SQ_BIG = 25;

    // Internal representation for `zone` value when the player can play in any zone.
    final int ZONE_ANY = 9;

    // Masks for use in changing bitboards
    final long LINE        = 0b111L;
    final long CHUNK       = 0b111111111L;
    final long DBLCHUNK    = (CHUNK << 9) | CHUNK;
    final long EXCLZONE    = ~(0b1111L << 54);
    final long CORNER_MASK = 0b101000101L;
    final long EDGE_MASK   = 0b010101010L;
    final long CENTRE_MASK = 0b000010000L;

    // Lookup tables to store evaluation for different arrangements of grids,
    // for both small grid and large grid metrics.
    // These tables will essentially store partial heuristic evaluations
    // for all possible small grid positions in the game.
    int[] EvalTableLarge = new int[262144];
    int[] EvalTableSmall = new int[262144];

    // Variable to contain the searching depth of the main alphaBeta call.
    int SEARCHING_DEPTH;

    public UT3B2() {
        // Initialise PopCount lookup table by inserting bit count of each number
        // from 0 to 511 in the corresponding index in the array.
        // This is to be used for 3-bit lines and 9-bit grids.
        int[] PopCount = new int[512];

        int evalLarge, evalSmall, i;
        int evalPos;
        int usCount, themCount;
        long usLines, themLines;
        boolean usWon, themWon;

        // Loop to fill in the PopCount table with the number of bits for a number *i*
        // at the index `i` of `PopCount`.
        for (i = 0; i < 512; ++i)
            PopCount[i] = (i & 1) + ((i >> 1) & 1) + ((i >> 2) & 1) +
                          ((i >> 3) & 1) + ((i >> 4) & 1) + ((i >> 5) & 1) +
                          ((i >> 6) & 1) + ((i >> 7) & 1) + ((i >> 8) & 1);

        // Loop through all possible such positions.
        for (long us = 0L; us < 512L; ++us) {
            for (long them = 0L; them < 512L; ++them) {
                // Different values will be reached depending on whether this pattern
                // is reflecting that of a small or large grid.
                evalLarge = 0;
                evalSmall = 0;

                // Get bitarrays of the 8 lines in this grid.
                usLines = lines(us);
                themLines = lines(them);

                // General positional and line rules are disregarded when the grid is won.
                usWon = false;
                themWon = false;

                // LINE SCORING.

                // Iterate through each line.
                for (i = 0; i < 24; i += 3) {
                    // Count occupancies of X and O in this line.
                    usCount = PopCount[(int)((usLines >> i) & LINE)];
                    themCount = PopCount[(int)((themLines >> i) & LINE)];

                    // Do not score a line if both players occupy it, as it can never be won.
                    if (usCount != 0 && themCount != 0)
                        continue;

                    // If fully occupied (3 of the same token in the line),
                    // this grid is won. So, we stop usual scoring and set the correct flag.
                    if (usCount == 3) {
                        usWon = true;
                        break;
                    }
                    if (themCount == 3) {
                        themWon = true;
                        break;
                    }

                    // Otherwise, use the weights to increment and decrement the score.
                    evalLarge += usCount == 2 ? BIG_TWO_COUNT : usCount == 1 ? BIG_ONE_COUNT : 0;
                    evalLarge -= themCount == 2 ? BIG_TWO_COUNT : themCount == 1 ? BIG_ONE_COUNT : 0;

                    evalSmall += usCount == 2 ? SMALL_TWO_COUNT : usCount == 1 ? SMALL_ONE_COUNT : 0;
                    evalSmall -= themCount == 2 ? SMALL_TWO_COUNT : themCount == 1 ? SMALL_ONE_COUNT : 0;
                }

                // POSITIONAL SCORING.

                // Use positional weights. If this is a large grid, this will be weighed by another weight.
                evalPos = CORNER * (PopCount[(int)(us & CORNER_MASK)] - PopCount[(int)(them & CORNER_MASK)])
                        + EDGE   * (PopCount[(int)(us & EDGE_MASK)]   - PopCount[(int)(them & EDGE_MASK)])
                        + CENTRE * (PopCount[(int)(us & CENTRE_MASK)] - PopCount[(int)(them & CENTRE_MASK)]);

                if (usWon)
                    EvalTableLarge[(int)((them << 9) | us)] = OUTCOME_WIN; // Filled X line, X wins.
                else if (themWon)
                    EvalTableLarge[(int)((them << 9) | us)] = OUTCOME_LOSS; // Filled O line, O wins.
                else if (PopCount[(int)(us | them)] == 9)
                    EvalTableLarge[(int)((them << 9) | us)] = OUTCOME_DRAW; // Grid completely filled, draw.
                else {
                    // If there is no event that doesn't immediately lead to a final result for this grid,
                    // put the entry as the final weighted sum of the line and positional scoring.
                    EvalTableLarge[(int)((them << 9) | us)] = evalLarge + evalPos * SQ_BIG;
                    EvalTableSmall[(int)((them << 9) | us)] = evalSmall + evalPos;
                }
            }
        }
    }

    long lines(long grid) {
        // Returns an unsigned integer using the least significant 24 bits in format:
        // 246 048 678 345 012 258 147 036
        // Where 0 = NW, 1 = N, 2 = NE, 3 = W, 4 = C, 5 = E, 6 = SW, 7 = S, 8 = SE.
        // Each set of three bits represents the occupancies in a line in a 9-bit grid.
        // The 24 bits thus contains information on all 8 possible lines.
        return (
            ( grid       & 1) * 0b000100000000100000000100L +
            ((grid >> 1) & 1) * 0b000000000000010000100000L +
            ((grid >> 2) & 1) * 0b100000000000001100000000L +
            ((grid >> 3) & 1) * 0b000000000100000000000010L +
            ((grid >> 4) & 1) * 0b010010000010000000010000L +
            ((grid >> 5) & 1) * 0b000000000001000010000000L +
            ((grid >> 6) & 1) * 0b001000100000000000000001L +
            ((grid >> 7) & 1) * 0b000000010000000000001000L +
            ((grid >> 8) & 1) * 0b000001001000000001000000L
        );
    }

    // The functions in this program all assume that we are starting with a valid board position.
    // Only valid positions will be reached if the program only ever uses its own functions to
    // play moves on the boards.

    // A move is represented as one integer, from 0 to 80 inclusive, representing one of the
    // 81 possible squares that a player can place their token on.
    ArrayList<Integer> generateMoves(Board board) {
        long us = board.Us(), them = board.Them(), share = board.Share();

        int i; // variable counter to be used potentially in multiple for loops in one pass

        // The variables `data1` and `data2` are reused to save memory.
        // They serve different purposes in different parts of this function.

        long data1 = lines((share >> 36) & CHUNK); // Contains all big grid lines for X
        long data2 = lines((share >> 45) & CHUNK); // Contains all big grid lines for O

        // If either X or O has made a big grid three-in-a-row, the game is over.
        for (i = 0; i < 24; i += 3)
            if (((data1 >> i) & LINE) == LINE || ((data2 >> i) & LINE) == LINE)
                return new ArrayList<Integer>();

        // List for all legal moves, to be returned.
        ArrayList<Integer> moveList = new ArrayList<Integer>();

        // Now, the variables *data1* and *data2* are being reused.

        data1 = us | them;                          // Contains all occupancies in zones NW to SW
        data2 = ((share >> 18) | share) & DBLCHUNK; // Contains all occupancies in zones S to SE
        long large = (share >> 36) | (share >> 45); // Contains all occupancies in big grid

        int zone = (int)((share >> 54) & 0b1111); // Determine zone the player is allows to play in

        switch (zone) {
            // If the player is allowed to play in any zone they wish, select all blank squares
            // that are not in a zone that has a corresponding occupied large grid.
            case ZONE_ANY:
                // The iteration over all squares has to be done separately due to the Board format.
                for (i = 0; i < 63; ++i)
                    if (((data1 >> i) & 1) == 0 && ((large >> (i/9)) & 1) == 0)
                        moveList.add(i);
                for (; i < 81; ++i)
                    if (((data2 >> (i-63)) & 1) == 0 && ((large >> (i/9)) & 1) == 0)
                        moveList.add(i);
                break;
            // In the following, we will assume that the `zone` value given corresponds to a zone that
            // does not correspond to an occupied space in the large grid.

            // Zones S and SE have to be dealt with separately due to the Board format.
            case 7: case 8:
                // Since only the value of 9*zone will be used from here on, update *zone* itself.
                zone *= 9;
                // Bitshift right to make relevant zone equal to the LSB 9 bits.
                // Since we are accessing `share`, we bishift right by 63 less.
                data2 >>= zone - 63;
                for (i = 0; i < 9; ++i)
                    if (((data2 >> i) & 1) == 0)
                        moveList.add(zone + i);
                break;
            default:
                // Since only the value of 9*zone will be used from here on, update *zone* itself.
                zone *= 9;
                // Bitshift right to make relevant zone equal to the LSB 9 bits.
                data1 >>= zone;
                for (i = 0; i < 9; ++i)
                    if (((data1 >> i) & 1) == 0)
                        moveList.add(zone + i);
                break;
        }

        return moveList; // Return list of legal moves.
    }

    // Returns the new board state after the given move has been played by the given side.
    // This will not affect the value of the board passed into this function.
    // side = 0 if player is X, 1 if player is O.
    Board playMove(Board board, int move, int side) {
        long us = board.Us(), them = board.Them(), share = board.Share();

        long lineOccupy; // This will contain our line occupancies of the zone we place in this move.
        long nextChunk;  // This will contain all occupancies of the next zone to play in.

        // If in the zone S or SE, we update `share`. Otherwise, update `us` or `them`.
        if (move > 62) {
            // The relevant position is the square number - 63, then 18 places further if O is playing.
            share |= 1L << (move - 63 + 18 * side);
            // Relevant zone is INT(move / 9), so we bitshift by `9*zone - 63` to get correct zone,
            // once again offsetting by 18 if player O is playing.
            lineOccupy = lines((share >> (9 * (move / 9) - 63 + 18*side)) & CHUNK);
        }
        // Player X is playing.
        else if (side == 0) {
            us |= 1L << move;
            lineOccupy = lines((us >> (9 * (move / 9))) & CHUNK); // Find lines in relevant zone for this move.
        }
        // Player O is playing.
        else {
            them |= 1L << move;
            lineOccupy = lines((them >> (9 * (move / 9))) & CHUNK); // Find lines in relevant zone for this move.
        }

        // To locate the occupancies for the zone corresponding to the next move,
        // whether the zone will be S to SE or otherwise needs to be considered,
        // due to the different locations of these zones in the Board representation.
        nextChunk = move % 9 > 6 ?
            ((share | (share >> 18)) >> (9*((move % 9) - 7))) & CHUNK : // Access `share` if next zone is S or SE.
            ((us | them) >> (9*(move % 9))) & CHUNK; // Access `us` and `them` otherwise.

        // For the lines in the zone we just filled, check to see if we have made a three-in-a-row.
        // This is the only thing we need to check as we are assuming that we are starting from
        // a valid position the whole time, and that the functions here are the only ones that
        // have edited the value of the board.
        boolean lineNotWon = true;
        for (int i = 0; i < 24 && lineNotWon; i += 3)
            // The LSB 3 bits represents one of the eight lines in the zone.
            if (((lineOccupy >> i) & LINE) == LINE)
                lineNotWon = false;

        // Change large grid contents by updating the correct section of `share`.
        // The correct bit position is 36 bits from LSB, then 0 to 8 positions left depending on move,
        // then shifted another 9 spaces if the line is made by player O.
        if (!lineNotWon)
            share |= 1L << (36 + 9 * side + move / 9);

        // Normally, the zone of the next move is determined by move % 9.
        // However, if that corresponding zone is completely filled, or
        // the large occupancy corresponding to the zone is occupied, then the player can move anywhere.
        int zone = nextChunk == CHUNK || (((share | (share >> 9)) >> (36 + move % 9)) & 1) == 1 ?
            ZONE_ANY :
            move % 9;

        // Return board with new updated values, putting `zone` value into the correct place in `share`.
        return new Board(us, them, (share & EXCLZONE) | ((long)zone << 54));
    }

    // Heuristic for evaluating a particular board from the perspective of a particular side,
    // once the minimax reaches a depth = 0 or other leaf node.
    int evaluate(Board board, int side) {
        long us = board.Us(), them = board.Them(), share = board.Share();

        // Throughout the function, the evaluation is calculated from the perspective of X.
        // It is then flipped according to `side` at the end.

        int eval = EvalTableLarge[(int)((share >> 36) & DBLCHUNK)];

        if (eval == OUTCOME_WIN || eval == OUTCOME_LOSS)
            return eval * (1 - (side << 1));

        long usData, themData;
        long large = ((share >> 36) | (share >> 45)) & CHUNK;

        int i;
        // Now, we loop through each of the 9 zones.
        for (i = 0; i < 9; ++i) {
            // To avoid code duplication, we check for whether the zone is from NW to SW or S to SE,
            // as the representation for each requires separate handling.
            if (i < 7) {
                usData = (us >> (9*i)) & CHUNK;     // Stores X occupancies of the current zone
                themData = (them >> (9*i)) & CHUNK; // Stores O occupancies of the current zone
            }
            else {
                usData = (share >> (9*i - 63)) & CHUNK;
                themData = (share >> (9*i - 45)) & CHUNK;
            }

            // If the zone is completely filled or the corresponding spot in the large grid is filled,
            // we do not score this zone.
            if ((usData | themData) == CHUNK || ((large >> i) & 1) == 1)
                continue;

            eval += EvalTableSmall[(int)((themData << 9) | usData)];
        }
        return eval * (1 - (side << 1));
    }

    // The main alpha-beta minimax function. Returns evaluation and principal variation.
    LineScorePair alphaBeta(Board board, int side, int depth, int alpha, int beta) {
        ArrayList<Integer> moveList = generateMoves(board); // Find all legal moves.
        int eval;

        // If there are no legal moves, the game is over.
        if (moveList.size() == 0) {
            eval = EvalTableLarge[(int)((board.Share() >> 36) & DBLCHUNK)] * (1 - (side << 1));
            if (eval == OUTCOME_WIN)
                eval -= SEARCHING_DEPTH - depth;
            else if (eval == OUTCOME_LOSS)
                eval += SEARCHING_DEPTH - depth;
            else
                eval = OUTCOME_DRAW;
             // Reached leaf node, so return empty PV.
            return new LineScorePair(eval, new int[SEARCHING_DEPTH - depth]);
        }

        // Reached leaf node, so return static evaluation and empty PV.
        if (depth == 0)
            return new LineScorePair(evaluate(board, side), new int[SEARCHING_DEPTH]);

        int length = moveList.size();
        int[] pv = new int[SEARCHING_DEPTH], line;
        LineScorePair pair;

        // Iterate over all legal moves.
        for (int i = 0; i < length; ++i) {
            // Recursive minimax call, using negamax construct as evaluation is symmetric.
            pair = alphaBeta(playMove(board, moveList.get(i), side), ~side & 1, depth-1, -beta, -alpha);
            // So, we take the negative of the evaluation.
            eval = -pair.Score();
            line = pair.Line();

            line[SEARCHING_DEPTH - depth] = moveList.get(i); // Record this move in the line.

            if (eval >= beta) // Fail hard beta-cutoff
                return new LineScorePair(beta, line);
            else if (eval > alpha) {
                // Next best move found.
                alpha = eval;
                // Update PV.
                pv = line;
            }
        }

        // Return evaluation of best option, and the matching PV.
        return new LineScorePair(alpha, pv);
    }

    void printBoard(Board board) {
        String[] sqArr = {"NW", "N", "NE", "W", "C", "E", "SW", "S", "SE"};

        long us = board.Us(), them = board.Them(), share = board.Share();
        String US = "X", THEM = "O";

        // Make arrays to contain the values of each square
        String[][] small = new String[][] {
            new String[] {".", ".", ".", ".", ".", ".", ".", ".", "."},
            new String[] {".", ".", ".", ".", ".", ".", ".", ".", "."},
            new String[] {".", ".", ".", ".", ".", ".", ".", ".", "."},
            new String[] {".", ".", ".", ".", ".", ".", ".", ".", "."},
            new String[] {".", ".", ".", ".", ".", ".", ".", ".", "."},
            new String[] {".", ".", ".", ".", ".", ".", ".", ".", "."},
            new String[] {".", ".", ".", ".", ".", ".", ".", ".", "."},
            new String[] {".", ".", ".", ".", ".", ".", ".", ".", "."},
            new String[] {".", ".", ".", ".", ".", ".", ".", ".", "."}
        };
        String[] large = new String[] {".", ".", ".", ".", ".", ".", ".", ".", "."};
        int zone = (int)((share >> 54) & 0b1111);

        // Fill in each element of the arrays with the correct values
        for (int i = 0; i < 63; ++i) {
            if (((us >> i) & 1) == 1)
                small[i / 9][i % 9] = US;
            else if (((them >> i) & 1) == 1)
                small[i / 9][i % 9] = THEM;
        }
        for (int i = 0; i < 18; ++i) {
            if (((share >> i) & 1) == 1)
                small[i / 9 + 7][i % 9] = US;
            else if (((share >> (i + 18)) & 1) == 1)
                small[i / 9 + 7][i % 9] = THEM;
        }
        for (int i = 0; i < 9; ++i) {
            if (((share >> (i + 36)) & 1) == 1)
                large[i] = US;
            else if (((share >> (i + 45)) & 1) == 1)
                large[i] = THEM;
        }

        String[][] slice;

        System.out.println("---+---+---");
        for (int i = 0; i < 9; i += 3) {
            slice = Arrays.copyOfRange(small, i, i+3);
            for (int j = 0; j < 9; j += 3) {
                System.out.println(
                    slice[0][j] + slice[0][j+1] + slice[0][j+2] + "|" +
                    slice[1][j] + slice[1][j+1] + slice[1][j+2] + "|" +
                    slice[2][j] + slice[2][j+1] + slice[2][j+2]
                );
            }
            System.out.println("---+---+---");
        }

        System.out.println(large[0] + large[1] + large[2]);
        System.out.println(large[3] + large[4] + large[5]);
        System.out.println(large[6] + large[7] + large[8]);

        System.out.println("ZONE: " + (zone == ZONE_ANY ? "ANY" : sqArr[zone]));
    }

    // First part is the zone, second part is the spot within that zone's grid.
    String moveString(int move) {
        String[] sqArr = {"nw", "n", "ne", "w", "c", "e", "sw", "s", "se"};
        return sqArr[move / 9] + "/" + sqArr[move % 9];
    }

    String evalString(int eval) {
        return (eval <= OUTCOME_LOSS + SEARCHING_DEPTH) ? "L" + (eval - OUTCOME_LOSS) :
            (eval >= OUTCOME_WIN - SEARCHING_DEPTH) ? "W" + (OUTCOME_WIN - eval) :
            (eval == OUTCOME_DRAW) ? "D0" :
            (eval > 0 ? "+" : "-") + Integer.toString(Math.abs(eval));
    }

    int zoneNumber(String zone) {
        switch (zone) {
            case "nw": return 0;
            case "n":  return 1;
            case "ne": return 2;
            case "w":  return 3;
            case "c":  return 4;
            case "e":  return 5;
            case "sw": return 6;
            case "s":  return 7;
            case "se": return 8;
            default:   return 9;
        }
    }

    int inputPlayerMove(ArrayList<Integer> possibleMoves, Scanner scan) {
        String strMove, zone, square;
        String[] strSplit;

        System.out.print("Move: ");
        strMove = scan.nextLine();
        while (strMove.compareTo("") == 0) {
            System.out.print("Move: ");
            strMove = scan.nextLine();
        }

        strSplit = strMove.split("/");
        zone = strSplit[0];
        square = strSplit[1];

        // Convert string to integer move representation
        int move = 9 * zoneNumber(zone) + zoneNumber(square);

        // Prompt for move until inputted move is legal
        while (!possibleMoves.contains(move)) {
            System.out.print("Move: ");
            strMove = scan.nextLine();
            while (strMove.compareTo("") == 0) {
                System.out.print("Move: ");
                strMove = scan.nextLine();
            }
            strSplit = strMove.split("/");
            zone = strSplit[0];
            square = strSplit[1];
            move = 9 * zoneNumber(zone) + zoneNumber(square);
        }

        return move;
    }

    // The main game loop to be executed.
    public void main(int depth, boolean selfStart, Scanner scan) {
        SEARCHING_DEPTH = depth;

        // Starting values for an empty board. The zone is ZONE_ANY, as the first player can place anywhere.
        Board board = new Board(
            0b0000000000000000000000000000000000000000000000000000000000000000L,
            0b0000000000000000000000000000000000000000000000000000000000000000L,
            0b0000001001000000000000000000000000000000000000000000000000000000L
        );

        int player = 0; // Player is playing X by default.

        ArrayList<Integer> possibleMoves;

        int eval, move;
        int[] line;
        ArrayList<String> strLines = new ArrayList<String>();

        long start, end;

        LineScorePair pair;

        // `selfStart` is true if the program is to play the first move. The player is thus playing O.
        if (selfStart) {
            // Output evaluation, PV and new board state.
            start = System.currentTimeMillis();
            pair = alphaBeta(board, 0, SEARCHING_DEPTH, -INFINITY, INFINITY);
            end = System.currentTimeMillis();
            eval = pair.Score();
            line = pair.Line();
            board = playMove(board, line[0], 0);
            for (int elem : line)
                strLines.add(moveString(elem));
            System.out.println("AI Move: " + strLines.get(0) + " PV: [" + String.join(", ", strLines) + "] Eval: " + evalString(eval) + " Time elapsed: " + (end - start) + " ms");
            player = 1; // Player is playing O.
        }

        strLines.clear();

        printBoard(board); // Print starting position.

        while (true) {
            possibleMoves = generateMoves(board);
            if (possibleMoves.size() == 0) {
                System.out.println("Game over");
                scan.nextLine();
                break;
            }

            // Input player move
            move = inputPlayerMove(possibleMoves, scan);

            // Play the inputted (and validated) move.
            board = playMove(board, move, player);
            printBoard(board);
            if (generateMoves(board).size() == 0) {
                System.out.println("Game over");
                scan.nextLine();
                break;
            }

            // Output evaluation, PV and new board state.
            start = System.currentTimeMillis();
            pair = alphaBeta(board, ~player & 1, SEARCHING_DEPTH, -INFINITY, INFINITY);
            end = System.currentTimeMillis();
            eval = pair.Score();
            line = pair.Line();
            board = playMove(board, line[0], ~player & 1);
            for (int elem : line)
                strLines.add(moveString(elem));
            System.out.println("AI Move: " + strLines.get(0) + " PV: [" + String.join(", ", strLines) + "] Eval: " + evalString(eval) + " Time elapsed: " + (end - start) + " ms");
            printBoard(board);

            strLines.clear();
        }

        scan.close();
    }
}

public class UT3B2L {
    public static void main(String[] args) {
        UT3B2 ut3b2 = new UT3B2();

        // Instantiate a Scanner object to be used throughout the program.
        Scanner scan = new Scanner(System.in);

        // Input depth for this program to work at.
        int depth;
        System.out.print("Depth: ");
        depth = Integer.parseInt(scan.nextLine());

        char c = scan.nextLine().charAt(0);
        while (c != '1' && c != '2')
            c = scan.nextLine().charAt(0);

        // Type '1' for program to play first ("X"), type '2' for program to play second ("O")
        System.out.println("Playing " + (c == '1' ? "X" : "O"));
        ut3b2.main(depth, c == '1', scan);

        // Close the Scanner object.
        scan.close();
    }
}