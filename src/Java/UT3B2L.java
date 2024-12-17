import java.lang.Exception;
import java.lang.StringBuilder;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import java.util.Scanner;
import java.util.stream.Stream;

record BoardMovePair(Board board, int move) {}

class FormatException extends Exception {
    public FormatException() {
        super();
    }
    public FormatException(String msg) {
        super(msg);
    }
}

public class UT3B2L {

    private static String[] ZONE_ARRAY_UPPER = {"NW", "N", "NE", "W", "C", "E", "SW", "S", "SE"};
    private static String[] ZONE_ARRAY_LOWER = {"nw", "n", "ne", "w", "c", "e", "sw", "s", "se"};

    private static void printBoard(Board board) {
        long us = board.us(), them = board.them(), share = board.share();
        int zone = (int)((share >> 54) & 0b1111);

        String[] small = new String[81];
        String[] large = new String[9];
        for (int i = 0; i < 63; ++i) {
            small[i] = ((us >> i) & 1) == 1 ? "X" :
                ((them >> i) & 1) == 1 ? "O" :
                ".";
        }
        for (int i = 0; i < 18; ++i) {
            small[i+63] = ((share >> i) & 1) == 1 ? "X" :
                ((share >> (i + 18)) & 1) == 1 ? "O" :
                ".";
        }
        for (int i = 0; i < 9; ++i) {
            large[i] = ((share >> (i + 36)) & 1) == 1 ? "X" :
                ((share >> (i + 45)) & 1) == 1 ? "O" :
                ".";
        }
        System.out.println("---+---+---");
        for (int i = 0; i < 81; i += 27) {
            for (int j = 0; j < 9; j += 3) {
                String[] row = new String[3];
                for (int k = 0; k < 27; k += 9) {
                    row[k / 9] = String.join("", Arrays.copyOfRange(small, i + j + k, i + j + k + 3));
                }
                System.out.println(String.join("|", row));
            }
            System.out.println("---+---+---");
        }

        for (int i = 0; i < 9; i += 3) {
            System.out.println(String.join("", Arrays.copyOfRange(large, i, i + 3)));
        }

        System.out.println("ZONE: " + (zone == Engine.ZONE_ANY ? "ANY" : ZONE_ARRAY_UPPER[zone]));
    }

    private static String moveString(int move) {
        return ZONE_ARRAY_LOWER[move / 9] + "/" + ZONE_ARRAY_LOWER[move % 9];
    }

    private static int moveFromString(String moveString) throws FormatException {
        String[] zoneAndSquare = moveString.split("/", 0);
        if (zoneAndSquare.length != 2)
            throw new FormatException();
        int zone = -1, square = -1;
        for (int i = 0; i < 9; ++i) {
            if (zoneAndSquare[0].equals(ZONE_ARRAY_LOWER[i]))
                zone = i;
            if (zoneAndSquare[1].equals(ZONE_ARRAY_LOWER[i]))
                square = i;
        }
        if (zone == -1 || square == -1)
            throw new FormatException();
        return 9 * zone + square;
    }

    private static ArrayList<String> movesAsStringList(int[] moves) {
        ArrayList<String> strings = new ArrayList<String>();
        for (int move : moves) {
            strings.add(moveString(move));
        }
        return strings;
    }

    private static String evalString(int eval, int maxDepth) {
        if (eval <= Engine.OUTCOME_LOSS + maxDepth)
            return "L" + (eval - Engine.OUTCOME_LOSS);
        else if (eval >= Engine.OUTCOME_WIN - maxDepth)
            return "W" + (Engine.OUTCOME_WIN - eval);
        else if (eval == Engine.OUTCOME_DRAW)
            return "D0";
        else if (eval > 0)
            return "+" + eval;
        else
            return "" + eval;
    }

    private static String boardString(Board board) {
        StringBuilder sb = new StringBuilder();
        long us = board.us(), them = board.them(), share = board.share();
        int zone = (int)((share >> 54) & 0b1111);
        List<String> rows = new ArrayList<String>();
        for (int i = 0; i < 81; i += 27) {
            for (int j = 0; j < 9; j += 3) {
                List<Integer> rowIndices = new ArrayList<Integer>();
                for (int k = 0; k < 27; k += 9) {
                    rowIndices.add(i + j + k);
                    rowIndices.add(i + j + k + 1);
                    rowIndices.add(i + j + k + 2);
                }
                for (Integer idx : rowIndices) {
                    if (idx > 62) {
                        if (((share >> (idx - 63)) & 1) == 1)
                            sb.append('x');
                        else if (((share >> (idx - 45)) & 1) == 1)
                            sb.append('o');
                        else
                            sb.append('.');
                    } else if (((us >> idx) & 1) == 1)
                        sb.append('x');
                    else if (((them >> idx) & 1) == 1)
                        sb.append('o');
                    else
                        sb.append('.');
                }
                rows.add(sb.toString());
                sb.setLength(0);
            }
        }
        return String.join("/", rows)
            .replace(".........", "9")
            .replace("........", "8")
            .replace(".......", "7")
            .replace("......", "6")
            .replace(".....", "5")
            .replace("....", "4")
            .replace("...", "3")
            .replace("..", "2")
            .replace(".", "1")
            + " "
            + (zone == Engine.ZONE_ANY ? "any" : ZONE_ARRAY_LOWER[zone]);
    }

    private static Board boardFromString(String boardString) throws FormatException {
        long us = 0, them = 0, share = 0;
        String decompressedString = boardString
            .replace("1", ".")
            .replace("2", "..")
            .replace("3", "...")
            .replace("4", "....")
            .replace("5", ".....")
            .replace("6", "......")
            .replace("7", ".......")
            .replace("8", "........")
            .replace("9", ".........");
        String[] cellAndZone = decompressedString.split(" ");
        if (cellAndZone.length != 2)
            throw new FormatException();
        String cell = cellAndZone[0];
        String zone = cellAndZone[1];
        int z = -1;
        for (int i = 0; i < 9; ++i) {
            if (ZONE_ARRAY_LOWER[i].equals(zone))
                z = i;
        }
        if (z != -1)
            share |= (long)z << 54;
        else if (zone.equals("any"))
            share |= (long)Engine.ZONE_ANY << 54;
        else
            throw new FormatException();
        String[] rows = cell.split("/");
        if (rows.length != 9)
            throw new FormatException();
        if (Arrays.asList(rows).stream().anyMatch(row -> row.length() != 9))
            throw new FormatException();
        List<Integer> rowIndices = new ArrayList<Integer>();
        for (int i = 0; i < 81; i += 27) {
            for (int j = 0; j < 9; j += 3) {
                for (int k = 0; k < 27; k += 9) {
                    rowIndices.add(i + j + k);
                    rowIndices.add(i + j + k + 1);
                    rowIndices.add(i + j + k + 2);
                }
            }
        }
        decompressedString = cell.replace("/", "");
        for (int j = 0; j < decompressedString.length(); ++j) {
            char c = decompressedString.charAt(j);
            int i = rowIndices.get(j);
            if (i > 62) {
                if (c == 'x')
                    share |= 1L << (i - 63);
                else if (c == 'o')
                    share |= 1L << (i - 45);
            } else if (c == 'x')
                us |= 1L << i;
            else if (c == 'o')
                them |= 1L << i;
        }
        long firstSevenUs = us;
        long firstSevenThem = them;
        for (int i = 0; i < 7; ++i) {
            if (Engine.linePresence(firstSevenUs >> (9 * i)))
                share |= 1L << (36 + i);
            else if (Engine.linePresence(firstSevenThem >> (9 * i)))
                share |= 1L << (45 + i);
        }
        long lastTwoUs = share;
        long lastTwoThem = share >> 18;
        for (int i = 7; i < 9; ++i) {
            if (Engine.linePresence(lastTwoUs >> (9*i - 63)))
                share |= 1L << (36 + i);
            else if (Engine.linePresence(lastTwoThem >> (9*i - 63)))
                share |= 1L << (45 + i);
        }
        return new Board(us, them, share);
    }

    public static void main(String[] args) {
        Scanner sc = new Scanner(System.in);

        Engine engine = new Engine();
        System.out.println("ready");

        List<BoardMovePair> history = new ArrayList<BoardMovePair>();
        history.add(new BoardMovePair(new Board(0L, 0L, (long)Engine.ZONE_ANY << 54), Engine.NULL_MOVE));

        boolean runMainLoop = true;

        while (runMainLoop) {
            String commandString = sc.nextLine();
            String[] command = commandString.split("\\s+");
            if (command.length == 0)
                continue;
            switch (command[0]) {
                case "newgame":
                    if (command.length < 3) {
                        System.out.println("newgame invalid args");
                        continue;
                    }
                    try {
                        history.clear();
                        history.add(
                            new BoardMovePair(
                                boardFromString(command[1] + " " + command[2]),
                                Engine.NULL_MOVE
                            )
                        );
                        System.out.println("newgame ok");
                    } catch (FormatException err) {
                        System.out.println("newgame invalid pos");
                    }
                    break;
                case "go":
                    if (command.length < 2) {
                        System.out.println("info error no depth");
                        continue;
                    }
                    try {
                        int depth = Integer.parseInt(command[1]);
                        if (depth <= 0) {
                            System.out.println("info error invalid depth");
                            continue;
                        }
                        int currentPlayer = (history.size() & 1) == 1 ? 0 : 1;
                        Board board = history.get(history.size() - 1).board();
                        long start = System.currentTimeMillis();
                        ScoreLinePair pair = engine.alphaBeta(
                            board,
                            currentPlayer,
                            depth,
                            Engine.OUTCOME_LOSS,
                            Engine.OUTCOME_WIN,
                            depth
                        );
                        long end = System.currentTimeMillis();
                        int[] line = pair.line();

                        System.out.printf(
                            "info depth %d pv %s eval %s time %d",
                            depth,
                            String.join(" ", movesAsStringList(line)),
                            evalString(pair.score(), depth),
                            end - start
                        );
                        System.out.println();

                        if (line.length > 0) {
                            history.add(
                                new BoardMovePair(
                                    engine.playMove(board, line[0], currentPlayer),
                                    line[0]
                                )
                            );
                        }

                    } catch (NumberFormatException err) {
                        System.out.println("info error invalid depth");
                    }
                    break;
                case "play":
                    if (command.length != 2) {
                        System.out.println("move invalid");
                        continue;
                    }
                    if (command[1].equals("null")) {
                        BoardMovePair pair = history.get(history.size() - 1);
                        history.add(pair);
                        System.out.println("move pos " + boardString(pair.board()));
                    } else {
                        try {
                            int move = moveFromString(command[1]);
                            Board board = history.get(history.size() - 1).board();
                            if (engine.generateMoves(board).contains(move)) {
                                int currentPlayer = (history.size() & 1) == 1 ? 0 : 1;
                                Board newBoard = engine.playMove(board, move, currentPlayer);
                                history.add(new BoardMovePair(newBoard, move));
                                System.out.println("move pos " + boardString(newBoard));
                            } else {
                                System.out.println("move illegal");
                            }
                        } catch (FormatException err) {
                            System.out.println("move invalid");
                        }
                    }
                    break;
                case "undo":
                    if (history.size() == 0) {
                        System.out.println("undo stackempty");
                        continue;
                    }
                    BoardMovePair lastPair = history.remove(history.size() - 1);
                    if (history.size() == 0) {
                        history.add(lastPair);
                        System.out.println("undo stackempty");
                    } else {
                        System.out.println("undo ok");
                    }
                    break;
                case "gamepos":
                    System.out.println(boardString(history.get(history.size() - 1).board()));
                    break;
                case "d":
                    printBoard(history.get(history.size() - 1).board());
                    break;
                case "q":
                    runMainLoop = false;
                    break;
                default:
                    System.out.println("badkeyword");
                    break;
            }
        }

        sc.close();
    }
}
