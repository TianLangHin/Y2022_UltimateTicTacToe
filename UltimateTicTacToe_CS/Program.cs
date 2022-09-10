using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

// A Board contains three pieces of information: the small grids, the large grid, and the zone to play in
using Board = System.ValueTuple<int[][],int[],int>;
/*
Small:
+----------+----------+----------+
| 00 01 02 | 09 10 11 | 18 19 20 |
| 03 04 05 | 12 13 14 | 21 22 23 |
| 06 07 08 | 15 16 17 | 24 25 26 |
+----------+----------+----------+
| 27 28 29 | 36 37 38 | 45 46 47 |
| 30 31 32 | 39 40 41 | 48 49 50 |
| 33 34 35 | 42 43 44 | 51 52 53 |
+----------+----------+----------+
| 54 55 56 | 63 64 65 | 72 73 74 |
| 57 58 59 | 66 67 68 | 75 76 77 |
| 60 61 62 | 69 70 71 | 78 79 80 |
+----------+----------+----------+
Large:
+----------+
| 00 01 02 |
| 03 04 05 |
| 06 07 08 |
+----------+
Zone:
nw(0), n(1), ne(2), w(3), c(4), e(5), sw(6), s(7), se(8), any(9)
*/

class UltimateTicTacToe
{
	// Weights for representing win, loss and draw outcomes
	const int INFINITY = 1000000;
	const int OUTCOME_WIN = INFINITY;
	const int OUTCOME_DRAW = 0;
	const int OUTCOME_LOSS = -INFINITY;

	// Weights for to-be-completed line occupancies
	const int LINE_BIG = 1;
	const int BIG_TWO_COUNT   = 90 * LINE_BIG;
	const int BIG_ONE_COUNT   = 20 * LINE_BIG;
	const int SMALL_TWO_COUNT = 8  * LINE_BIG;
	const int SMALL_ONE_COUNT = 1  * LINE_BIG;

	// Weights for positions of squares in grid
	const int CENTRE = 9;
	const int CORNER = 7;
	const int EDGE   = 5;
	const int SQ_BIG = 25;

	// Internal representations for boards and moves
	const int NONE = 0, US = 1, THEM = -1;
	const int ZONE_ANY = 9;
	const int NO_MOVE = 81;

	int SEARCHING_DEPTH;

	/*
		Function for returning an array of lines in the grid
		The grid:
		+-----------+
		| nw  n  ne |
		|  w  c  e  |
		| sw  s  se |
		+-----------+
		Returns lines:
			nw-w-sw, n-c-s, ne-e-se, (columns)
			nw-n-ne, w-c-e, sw-s-se, (rows)
			nw-c-se ne-c-sw          (diagonals)
	*/
	int[][] Lines(int[] grid)
	{
		return new int[][] {
			new int[] {grid[0], grid[3], grid[6]},
			new int[] {grid[1], grid[4], grid[7]},
			new int[] {grid[2], grid[5], grid[8]},
			new int[] {grid[0], grid[1], grid[2]},
			new int[] {grid[3], grid[4], grid[5]},
			new int[] {grid[6], grid[7], grid[8]},
			new int[] {grid[0], grid[4], grid[8]},
			new int[] {grid[2], grid[4], grid[6]}
		};
	}

	// The functions in this program all assume that we are starting with a valid board position
	// Only valid positions will be reached if the program only ever uses its own functions to
	// play moves on the boards
	List<int> MoveGen(Board board)
	{
		(int[][] small, int[] large, int zone) = board;

		// If any three-in-a-rows have been found in the large grids, the game is over
		if (Lines(large).Any(x => (x[0] == US && x[1] == US && x[2] == US) || (x[0] == THEM && x[1] == THEM && x[2] == THEM)))
			return new List<int>();

		List<int> moveList = new List<int>();
		switch (zone)
		{
			case ZONE_ANY:
				// If player is allowed to play in any zone they wish, then select blank squares
				// that are not in a zone that corresponds to a filled large grid
				for (int i = 0; i < 81; ++i)
					if (small[i / 9][i % 9] == NONE && large[i / 9] == NONE)
						moveList.Add(i);
				break;
			default:
				// Only select blank squares in the indicated zone
				for (int i = 0; i < 9; ++i)
					if (small[zone][i] == NONE)
						moveList.Add(9*zone + i);
				break;
		}
		return moveList;
	}

	Board PlayMove(Board board, int move, bool side)
	{
		int[][] small = new int[][] {
			(int[])(board.Item1[0].Clone()),
			(int[])(board.Item1[1].Clone()),
			(int[])(board.Item1[2].Clone()),
			(int[])(board.Item1[3].Clone()),
			(int[])(board.Item1[4].Clone()),
			(int[])(board.Item1[5].Clone()),
			(int[])(board.Item1[6].Clone()),
			(int[])(board.Item1[7].Clone()),
			(int[])(board.Item1[8].Clone())
		};
		int[] large = (int[])(board.Item2.Clone());

		small[move / 9][move % 9] = side ? US : THEM; // Place our tile in the chosen square

		// Now, look at the lines present in this zone to check if the big grid is filled
		int[][] lines = Lines(small[move / 9]);

		// Only need to check for "X" lines if we are playing as "X"
		if (side && lines.Any(m => m[0] == US && m[1] == US && m[2] == US))
			large[move / 9] = US;
		// Only need to check for "O" lines if we are playing as "O"
		else if (!side && lines.Any(m => m[0] == THEM && m[1] == THEM && m[2] == THEM))
			large[move / 9] = THEM;

		// Next player can play in any zone if the current small grid is completely occupied or large grid is filled
		// Otherwise, the zone corresponds to the previous move's relative position within its small grid
		int zone = (!small[move / 9].Any(m => m == NONE) || large[move % 9] != NONE) ? ZONE_ANY : move % 9;

		return (small, large, zone);
	}

	int Evaluate(Board board, bool side)
	{
		(int[][] small, int[] large, int zone) = board;

		// Since the evaluation of a position is symmetric, shift our perspective between "X" and "O" as needed
		(int us, int them) = side ? (US, THEM) : (THEM, US);

		int eval = 0,         // Variable to store incrementally added evaluation
			lineUs, lineThem, // Counts "X" and "O" in a line
			i, j,             // Counter variables
			sqScore;          // Variable to store positional score

		int[][] lines = Lines(large);

		for (i = 0; i < 8; ++i) // There are 8 lines to loop through
		{
			lineUs = lines[i].Count(x => x == us);
			lineThem = lines[i].Count(x => x == them);

			// Do not score lines that can no longer yield a three-in-a-row
			if (lineUs > 0 && lineThem > 0)
				continue;

			switch (lineUs)
			{
				case 3:
					return OUTCOME_WIN; // We occupy all 3 squares in the line, hence win
				case 2:
					eval += BIG_TWO_COUNT;
					break;
				case 1:
					eval += BIG_ONE_COUNT;
					break;
				default:
					break;
			}

			switch (lineThem)
			{
				case 3:
					return OUTCOME_LOSS; // They occupy all 3 squares in the line, hence lose
				case 2:
					eval -= BIG_TWO_COUNT;
					break;
				case 1:
					eval -= BIG_ONE_COUNT;
					break;
				default:
					break;
			}
		}

		// If all the large grids have been filled, and no three-in-a-rows have been found, then it is a draw
		if (!large.Contains(NONE))
			return OUTCOME_DRAW;

		// Loop through each of the zones
		for (i = 0; i < 9; ++i)
		{
			// Only evaluate zones that are yet to be completed
			if (!small[i].Contains(NONE) || large[i] != NONE)
				continue;

			lines = Lines(small[i]);

			for (j = 0; j < 8; ++j) // There are 8 lines to loop through
			{
				lineUs = lines[j].Count(x => x == us);
				lineThem = lines[j].Count(x => x == them);

				// Do not score lines that can no longer yield a three-in-a-row
				if (lineUs > 0 && lineThem > 0)
					continue;

				// We no longer need to check for three-in-a-rows as they should be captured by the filled large square earlier
				switch (lineUs)
				{
					case 2:
						eval += SMALL_TWO_COUNT;
						break;
					case 1:
						eval += SMALL_ONE_COUNT;
						break;
					default:
						break;
				}

				switch (lineThem)
				{
					case 2:
						eval -= SMALL_TWO_COUNT;
						break;
					case 1:
						eval -= SMALL_ONE_COUNT;
						break;
					default:
						break;
				}
			}
		}

		// Positional scoring. The following work due to the fact that US = 1 and THEM = -1,
		// so the opposing occupancies will subtract one another through addition, as intended

		// This line multiplies the positional score by the SQ_BIG weight for the large grid
		sqScore = SQ_BIG * (
			(large[0] + large[2] + large[6] + large[8]) * CORNER + // corners: nw(0), ne(2), sw(6), se(8)
			(large[1] + large[3] + large[5] + large[7]) * EDGE +   // edges:    n(1),  w(3),  e(5),  s(7)
			large[4] * CENTRE                                      // centre:   c(4)
		);

		// Iterate through each small grid
		for (i = 0; i < 9; ++i)
			sqScore += (
				(small[i][0] + small[i][2] + small[i][6] + small[i][8]) * CORNER + // corners: nw(0), ne(2), sw(6), se(8)
				(small[i][1] + small[i][3] + small[i][5] + small[i][7]) * EDGE +   // edges:    n(1),  w(3),  e(5),  s(7)
				small[i][4] * CENTRE                                               // centre:   c(4)
			);

		// Readjust for the side, due to the board not being colour agnostic
		return eval + (side ? sqScore : -sqScore);
	}

	// Returns evaluation score and principal variation
	ValueTuple<int,List<int>> AlphaBeta(Board board, bool side, int depth, int alpha, int beta)
	{
		// Find all legal moves
		List<int> moveList = MoveGen(board);

		// No legal moves, so the game is over in this position
		if (moveList.Count() == 0)
		{
			(int us, int them) = side ? (US, THEM) : (THEM, US); // Shift perspective as board is not colour agnostic
			int[][] largeLines = Lines(board.Item2); // Extract lines from the large grid
			return (
				// Win in # moves if we have 3-in-a-row in large grid
				(largeLines.Any(x => x[0] == us && x[1] == us && x[2] == us) ? OUTCOME_WIN + depth - SEARCHING_DEPTH :
				// Lose in # moves if we have 3-in-a-row in large grid
				largeLines.Any(x => x[0] == them && x[1] == them && x[2] == them) ? OUTCOME_LOSS - depth + SEARCHING_DEPTH :
				// Neither player has three-in-a-row, hence draw
				OUTCOME_DRAW),
				// No moves have been played, so return empty PV
				new List<int>()
			);
		}

		// Reached search depth
		if (depth == 0)
			return (Evaluate(board, side), new List<int>());

		int eval;
		List<int> pv = new List<int>(), line;
		foreach (int move in moveList)
		{
			// recursive minimax call, using negamax construct
			(eval, line) = AlphaBeta(PlayMove(board, move, side), !side, depth-1, -beta, -alpha);

			eval = -eval; // Take the negative of the evaluation, as the returned eval is for the opposing player
			line.Insert(0, move); // Insert current move to track the line being searched from above recursive call

			if (eval >= beta) // fail hard beta-cutoff
				return (beta, line);
			else if (eval > alpha) // new best move found
			{
				alpha = eval;
				pv = line;
			}
		}
		return (alpha, pv);
	}

	void PrintBoard(Board board)
	{
		string[] sqArr = {"NW", "N", "NE", "W", "C", "E", "SW", "S", "SE"};
		int[][] small = (int[][])board.Item1.Clone();
		int[] large = (int[])board.Item2.Clone();
		int zone = board.Item3;
		ArraySegment<int[]> bigRow;
		Console.WriteLine("---+---+---");
		for (int i = 0; i < 81; i += 27)
		{
			bigRow = new ArraySegment<int[]>(small, i/9, 3);
			for (int j = 0; j < 9; j += 3)
			{
				Console.WriteLine(string.Join("|",
					(from x in bigRow select string.Join("",
						(from y in new ArraySegment<int>(x, j, 3) select y == US ? "X" : y == THEM ? "O" : ".")))));
			}
			Console.WriteLine("---+---+---");
		}
		for (int i = 0; i < 3; ++i)
			Console.WriteLine(string.Join("",
				(from x in new ArraySegment<int>(large, 3*i, 3)
					select x == US ? "X" : x == THEM ? "O" : ".")));
		Console.WriteLine("ZONE: " + (zone == ZONE_ANY ? "ANY" : sqArr[zone]));
	}

	string MoveString(int move)
	{
		string[] sqArr = {"nw", "n", "ne", "w", "c", "e", "sw", "s", "se"};
		return sqArr[move / 9] + "/" + sqArr[move % 9];
	}

	// The evaluation score is always outputted in the following format:
	// The first character is one of the five:
	// "+": Meaning the position is winning for the AI, and the following number is the score
	// "-": Meaning the position is losing for the AI, and the following number is the score
	// "W": Meaning the AI has found a forced win, and the following number is the number of moves in which it will happen
	// "L": Meaning the AI has found the position to be a forced loss, and the following number is the number of moves in which it will happen
	// "D": Meaning the AI has found a draw, and the following number is always 0.
	string EvalString(int eval)
	{
		return (eval <= OUTCOME_LOSS + SEARCHING_DEPTH) ? "L" + (eval - OUTCOME_LOSS) :
			(eval >= OUTCOME_WIN - SEARCHING_DEPTH) ? "W" + (OUTCOME_WIN - eval) :
			(eval == OUTCOME_DRAW) ? "D0" :
			(eval > 0 ? "+" : "-") + Math.Abs(eval).ToString();
	}

	public void Main(int searchingDepth, bool selfStart)
	{
		SEARCHING_DEPTH = searchingDepth;
		string[] sqArr = {"nw", "n", "ne", "w", "c", "e", "sw", "s", "se"};

		Board board = (
			new int[][]{
				new int[9]{0, 0, 0, 0, 0, 0, 0, 0, 0},
				new int[9]{0, 0, 0, 0, 0, 0, 0, 0, 0},
				new int[9]{0, 0, 0, 0, 0, 0, 0, 0, 0},
				new int[9]{0, 0, 0, 0, 0, 0, 0, 0, 0},
				new int[9]{0, 0, 0, 0, 0, 0, 0, 0, 0},
				new int[9]{0, 0, 0, 0, 0, 0, 0, 0, 0},
				new int[9]{0, 0, 0, 0, 0, 0, 0, 0, 0},
				new int[9]{0, 0, 0, 0, 0, 0, 0, 0, 0},
				new int[9]{0, 0, 0, 0, 0, 0, 0, 0, 0}
			},
			new int[9]{0, 0, 0, 0, 0, 0, 0, 0, 0},
		ZONE_ANY);

		bool player = true;
		PrintBoard(board);

		List<int> possibleMoves;
		string strMove, zone, square;
		string[] strSplit;

		int eval, move;
		List<int> line;
		List<string> strLines;

		Stopwatch timer = new Stopwatch();

		if (selfStart)
		{
			timer.Start();
			(eval, line) = AlphaBeta(board, true, SEARCHING_DEPTH, -INFINITY, INFINITY);
			timer.Stop();
			board = PlayMove(board, line[0], true);
			strLines = (from mv in line select MoveString(mv)).ToList();
			Console.WriteLine("AI Move: " + strLines[0] + " PV: [" + string.Join(", ", strLines) + "] Eval: " + EvalString(eval) + " Time elapsed: " + timer.ElapsedMilliseconds + " ms");
			player = false;
		}

		PrintBoard(board);
		while (true)
		{
			possibleMoves = MoveGen(board);
			if (possibleMoves.Count() == 0)
			{
				Console.WriteLine("Game over");
				Console.ReadLine();
				break;
			}
			Console.Write("Move: ");
			strMove = Console.ReadLine() ?? "";
			while (strMove == "")
			{
				Console.Write("Move: ");
				strMove = Console.ReadLine() ?? "";
			}
			strSplit = strMove.Split('/');
			zone = strSplit[0];
			square = strSplit[1];
			move = 9 * Array.FindIndex(sqArr, (x => x == zone))
				+ Array.FindIndex(sqArr, (x => x == square));
			while (!possibleMoves.Contains(move))
			{
				Console.Write("Move: ");
				strMove = Console.ReadLine() ?? "";
				while (strMove == "")
				{
					Console.Write("Move: ");
					strMove = Console.ReadLine() ?? "";
				}
				strSplit = strMove.Split('/');
				zone = strSplit[0];
				square = strSplit[1];
				move = 9 * Array.FindIndex(sqArr, (x => x == zone))
					+ Array.FindIndex(sqArr, (x => x == square));
			}
			board = PlayMove(board, move, player);
			PrintBoard(board);
			if (MoveGen(board).Count() == 0)
			{
				Console.WriteLine("Game over");
				Console.ReadLine();
				break;
			}
			timer.Reset();
			timer.Start();
			(eval, line) = AlphaBeta(board, !player, SEARCHING_DEPTH, -INFINITY, INFINITY);
			timer.Stop();
			board = PlayMove(board, line[0], !player);
			strLines = (from mv in line select MoveString(mv)).ToList();
			Console.WriteLine("AI Move: " + strLines[0] + " PV: [" + string.Join(", ", strLines) + "] Eval: " + EvalString(eval) + " Time elapsed: " + timer.ElapsedMilliseconds + " ms");
			PrintBoard(board);
		}
	}
}

class Program
{
	public static void Main(string[] args)
	{
		UltimateTicTacToe uttt = new UltimateTicTacToe();
		int depth;
		Console.Write("Depth: ");
		while (!int.TryParse(Console.ReadLine(), out depth))
			Console.Write("Depth: ");
		char c;
		while ((c = Console.ReadKey(true).KeyChar) != '1' && c != '2') {}
		Console.WriteLine("Playing " + (c == '1' ? "X" : "O"));
		uttt.Main(depth, c == '1');
	}
}