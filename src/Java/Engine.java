import java.util.ArrayList;

record Board(long us, long them, long share) {}

record ScoreLinePair(int score, int[] line) {}

public class Engine {
    public static final int OUTCOME_WIN = 1000000;
    public static final int OUTCOME_DRAW = 0;
    public static final int OUTCOME_LOSS = -1000000;

    public static final int ZONE_ANY = 9;

    public static final int NULL_MOVE = 81;

    private static final int BIG_TWO_COUNT   = 90;
    private static final int BIG_ONE_COUNT   = 20;
    private static final int SMALL_TWO_COUNT = 8;
    private static final int SMALL_ONE_COUNT = 1;

    private static final int CENTRE = 9;
    private static final int CORNER = 7;
    private static final int EDGE   = 5;
    private static final int SQ_BIG = 25;

    private static final long LINE        = 0b111L;
    private static final long CHUNK       = 0b111111111L;
    private static final long DBLCHUNK    = (CHUNK << 9) | CHUNK;
    private static final long EXCLZONE    = ~(0b1111L << 54);
    private static final long CORNER_MASK = 0b101_000_101L;
    private static final long EDGE_MASK   = 0b010_101_010L;
    private static final long CENTRE_MASK = 0b000_010_000L;

    private int[] evalTableLarge = new int[262144];
    private int[] evalTableSmall = new int[262144];

    public Engine() {
        int[] popCount = new int[512];

        for (int i = 0; i < 512; ++i) {
            popCount[i] = (i & 1) +
                ((i >> 1) & 1) +
                ((i >> 2) & 1) +
                ((i >> 3) & 1) +
                ((i >> 4) & 1) +
                ((i >> 5) & 1) +
                ((i >> 6) & 1) +
                ((i >> 7) & 1) +
                ((i >> 8) & 1);
        }

        for (long us = 0L; us < 512L; ++us) {
            for (long them = 0L; them < 512L; ++them) {
                int evalLarge = 0;
                int evalSmall = 0;

                long usLines = lines(us);
                long themLines = lines(them);

                boolean usWon = false;
                boolean themWon = false;

                for (int i = 0; i < 24; i += 3) {
                    int usCount = popCount[(int)((usLines >> i) & LINE)];
                    int themCount = popCount[(int)((themLines >> i) & LINE)];

                    if (usCount != 0 && themCount != 0)
                        continue;

                    if (usCount == 3) {
                        usWon = true;
                        break;
                    }
                    if (themCount == 3) {
                        themWon = true;
                        break;
                    }

                    evalLarge += usCount == 2 ? BIG_TWO_COUNT :
                        usCount == 1 ? BIG_ONE_COUNT :
                        0;
                    evalLarge -= themCount == 2 ? BIG_TWO_COUNT :
                        themCount == 1 ? BIG_ONE_COUNT :
                        0;

                    evalSmall += usCount == 2 ? SMALL_TWO_COUNT :
                        usCount == 1 ? SMALL_ONE_COUNT :
                        0;
                    evalSmall -= themCount == 2 ? SMALL_TWO_COUNT :
                        themCount == 1 ? SMALL_ONE_COUNT :
                        0;
                }

                int evalPos = CORNER *
                    (popCount[(int)(us & CORNER_MASK)] - popCount[(int)(them & CORNER_MASK)]) +
                    EDGE *
                    (popCount[(int)(us & EDGE_MASK)] - popCount[(int)(them & EDGE_MASK)]) +
                    CENTRE *
                    (popCount[(int)(us & CENTRE_MASK)] - popCount[(int)(them & CENTRE_MASK)]);

                if (usWon)
                    evalTableLarge[(int)((them << 9) | us)] = OUTCOME_WIN;
                else if (themWon)
                    evalTableLarge[(int)((them << 9) | us)] = OUTCOME_LOSS;
                else if (popCount[(int)(us | them)] == 9)
                    evalTableLarge[(int)((them << 9) | us)] = OUTCOME_DRAW;
                else {
                    evalTableLarge[(int)((them << 9) | us)] = evalLarge + evalPos * SQ_BIG;
                    evalTableSmall[(int)((them << 9) | us)] = evalSmall + evalPos;
                }
            }
        }
    }

    public final long lines(long grid) {
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

    public static final boolean linePresence(long grid) {
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

    public ArrayList<Integer> generateMoves(Board board) {

        long us = board.us(), them = board.them(), share = board.share();

        if (linePresence(share >> 36) || linePresence(share >> 45))
            return new ArrayList<Integer>();

        int zone = (int)((share >> 54) & 0b1111);

        ArrayList<Integer> moveList = new ArrayList<>();

        switch (zone) {
            case ZONE_ANY: {
                long nwToSw = us | them;
                long sToSe = (share >> 18) | share;
                long large = (share >> 36) | (share >> 45);
                int i;
                for (i = 0; i < 63; ++i)
                    if (((nwToSw >> i) & 1) == 0 && ((large >> (i/9)) & 1) == 0)
                        moveList.add(i);
                for (; i < 81; ++i)
                    if (((sToSe >> (i-63)) & 1) == 0 && ((large >> (i/9)) & 1) == 0)
                        moveList.add(i);
                break;
            }
            case 7: case 8: {
                zone *= 9;
                long sToSe = ((share >> 18) | share) >> (zone - 63);
                for (int i = 0; i < 9; ++i)
                    if (((sToSe >> i) & 1) == 0)
                        moveList.add(zone + i);
                break;
            }
            default: {
                zone *= 9;
                long nwToSw = (us | them) >> zone;
                for (int i = 0; i < 9; ++i)
                    if (((nwToSw >> i) & 1) == 0)
                        moveList.add(zone + i);
                break;
            }
        }

        return moveList;
    }

    public Board playMove(Board board, int move, int side) {
        long us = board.us(), them = board.them(), share = board.share();
        boolean lineOccupancy;
        long nextChunk;
        if (move > 62) {
            share |= 1L << (move - 63 + 18 * side);
            lineOccupancy = linePresence(share >> (9 * (move / 9) - 63 + 18*side));
        }
        else if (side == 0) {
            us |= 1L << move;
            lineOccupancy = linePresence(us >> (9 * (move / 9)));
        }
        else {
            them |= 1L << move;
            lineOccupancy = linePresence(them >> (9 * (move / 9)));
        }

        if (lineOccupancy)
            share |= 1L << (36 + 9 * side + move / 9);

        nextChunk = move % 9 > 6 ?
            ((share | (share >> 18)) >> (9*((move % 9) - 7))) & CHUNK :
            ((us | them) >> (9*(move % 9))) & CHUNK;

        int zone = nextChunk == CHUNK || (((share | (share >> 9)) >> (36 + move % 9)) & 1) == 1 ?
            ZONE_ANY :
            move % 9;

        return new Board(us, them, (share & EXCLZONE) | ((long)zone << 54));
    }

    public int evaluate(Board board, int side) {

        long us = board.us(), them = board.them(), share = board.share();

        int eval = evalTableLarge[(int)((share >> 36) & DBLCHUNK)];
        if (eval == OUTCOME_WIN || eval == OUTCOME_LOSS)
            return eval * (1 - (side << 1));

        long large = ((share >> 36) | (share >> 45)) & CHUNK;
        if (large == CHUNK)
            return OUTCOME_DRAW;

        int i;
        long usData, themData;
        for (i = 0; i < 7; ++i) {
            usData = (us >> (9*i)) & CHUNK;
            themData = (them >> (9*i)) & CHUNK;
            if (((large >> i) & 1) == 1 || (usData | themData) == CHUNK)
                continue;
            eval += evalTableSmall[(int)((themData << 9) | usData)];
        }
        for (; i < 9; ++i) {
            usData = (share >> (9*i - 63)) & CHUNK;
            themData = (share >> (9*i - 45)) & CHUNK;
            if (((large >> i) & 1) == 1 || (usData | themData) == CHUNK)
                continue;
            eval += evalTableSmall[(int)((themData << 9) | usData)];
        }
        return eval * (1 - (side << 1));
    }

    public ScoreLinePair alphaBeta(Board board, int side, int depth, int alpha, int beta, int maxDepth) {

        if (depth == 0) {
            int adjustedEval = evaluate(board, side);
            if (adjustedEval == OUTCOME_WIN) {
                adjustedEval -= maxDepth - depth;
            } else if (adjustedEval == OUTCOME_LOSS) {
                adjustedEval += maxDepth - depth;
            }
            return new ScoreLinePair(adjustedEval, new int[maxDepth]);
        }

        ArrayList<Integer> moveList = generateMoves(board);

        if (moveList.isEmpty()) {
            int eval = evalTableLarge[(int)((board.share() >> 36) & DBLCHUNK)] * (1 - (side << 1));
            switch (eval) {
                case OUTCOME_WIN:
                    return new ScoreLinePair(eval - maxDepth + depth, new int[maxDepth - depth]);
                case OUTCOME_LOSS:
                    return new ScoreLinePair(eval + maxDepth - depth, new int[maxDepth - depth]);
                default:
                    return new ScoreLinePair(OUTCOME_DRAW, new int[maxDepth - depth]);
            }
        }

        int[] pv = new int[maxDepth], line;
        int eval;
        ScoreLinePair pair;

        for (int move : moveList) {
            pair = alphaBeta(playMove(board, move, side), ~side & 1, depth-1, -beta, -alpha, maxDepth);
            eval = -pair.score();
            line = pair.line();

            line[maxDepth - depth] = move;

            if (eval >= beta)
                return new ScoreLinePair(beta, line);
            else if (eval > alpha) {
                alpha = eval;
                pv = line;
            }
        }

        return new ScoreLinePair(alpha, pv);
    }
}
