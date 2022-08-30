using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using Board = System.ValueTuple<int[][],int[],int>;

class UltimateTicTacToe
{
	const int INFINITY = 1000000;
	const int OUTCOME_WIN = INFINITY;
	const int OUTCOME_DRAW = 0;
	const int OUTCOME_LOSS = -INFINITY;

	const int BIG_TWO_COUNT   = 200;
	const int BIG_ONE_COUNT   = 100;
	const int SMALL_TWO_COUNT = 25;
	const int SMALL_ONE_COUNT = 10;

	const int NONE = 0, US = 1, THEM = 2;

	int SEARCHING_DEPTH;

	List<int[]> Lines(int[] grid)
	{
		List<int[]> result = new List<int[]>();
		result.Add(new int[] {grid[0], grid[3], grid[6]});
		result.Add(new int[] {grid[1], grid[4], grid[7]});
		result.Add(new int[] {grid[2], grid[5], grid[8]});
		result.Add(new int[] {grid[0], grid[1], grid[2]});
		result.Add(new int[] {grid[3], grid[4], grid[5]});
		result.Add(new int[] {grid[6], grid[7], grid[8]});
		result.Add(new int[] {grid[0], grid[4], grid[8]});
		result.Add(new int[] {grid[2], grid[4], grid[6]});
		return result;
	}

	List<int> MoveGen(Board board)
	{
		int[][] small = new int[][] {
			new int[]{}, new int[]{}, new int[]{},
			new int[]{}, new int[]{}, new int[]{},
			new int[]{}, new int[]{}, new int[]{}
		};
		for (int k = 0; k < 9; ++k)
			small[k] = (int[])(board.Item1[k].Clone());
		int[] large = (int[])board.Item2.Clone();
		int zone = board.Item3;
		List<int[]> lines = Lines(large);
		if (lines.Any(
		x => (x[0] == US && x[1] == US && x[2] == US) ||
		(x[0] == THEM && x[1] == THEM && x[2] == THEM)))
			return new List<int>();
		List<int> moveList = new List<int>();
		if (zone == -1)
		{
			for (int i = 0; i < 81; ++i)
			{
				if (small[i / 9][i % 9] == NONE && large[i / 9] == NONE)
					moveList.Add(i);
			}
		}
		else
		{
			for (int i = 0; i < 9; ++i)
			{
				if (small[zone][i] == NONE)
					moveList.Add(9*zone + i);
			}
		}
		moveList = moveList.OrderBy(m => small[m/9].Count(x => x == THEM) - small[m/9].Count(x => x == US)).ToList();
		return moveList;
	}

	Board PlayMove(Board board, int move, bool side)
	{
		int[][] small = new int[][] {
			new int[]{}, new int[]{}, new int[]{},
			new int[]{}, new int[]{}, new int[]{},
			new int[]{}, new int[]{}, new int[]{}
		};
		for (int k = 0; k < 9; ++k)
			small[k] = (int[])(board.Item1[k].Clone());
		int[] large = (int[])(board.Item2.Clone());
		int zone = board.Item3;
		small[move / 9][move % 9] = side ? US : THEM;
		List<int[]> lines = Lines(small[move / 9]);
		if (side && lines.Any(m => m.Count(x => x == US) == 3))
			large[move / 9] = US;
		else if (!side && lines.Any(m => m.Count(x => x == THEM) == 3))
			large[move / 9] = THEM;
		if (small[move / 9].All(m => m != NONE) || large[move % 9] != NONE)
			zone = -1;
		else
			zone = move % 9;
		return (small, large, zone);
	}

	int Evaluate(Board board, bool side)
	{
		int[][] small = new int[][] {
			new int[]{}, new int[]{}, new int[]{},
			new int[]{}, new int[]{}, new int[]{},
			new int[]{}, new int[]{}, new int[]{}
		};
		for (int k = 0; k < 9; ++k)
			small[k] = (int[])(board.Item1[k].Clone());
		int[] large = (int[])board.Item2.Clone();
		int zone = board.Item3;
		int us, them;
		(us, them) = side ? (US, THEM) : (THEM, US);
		List<int[]> bigLines = Lines(large);
		int eval = 0;
		foreach (int[] line in bigLines)
		{
			switch (line.Count(x => x == us))
			{
				case 3:
					return OUTCOME_WIN;
				case 2:
					eval += BIG_TWO_COUNT;
					break;
				case 1:
					eval += BIG_ONE_COUNT;
					break;
				default:
					break;
			}
			switch (line.Count(x => x == them))
			{
				case 3:
					return OUTCOME_LOSS;
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
		if (!large.Contains(NONE))
			return OUTCOME_DRAW;
		List<int[]> lines;
		for (int i = 0; i < 9; ++i)
		{
			if (!small[i].Contains(NONE) || large[i] != NONE)
				continue;
			lines = Lines(small[i]);
			foreach (int[] line in lines)
			{
				switch (line.Count(x => x == us))
				{
					case 2:
						eval += SMALL_ONE_COUNT;
						break;
					case 1:
						eval += SMALL_TWO_COUNT;
						break;
					default:
						break;
				}
				switch (line.Count(x => x == them))
				{
					case 2:
						eval -= SMALL_ONE_COUNT;
						break;
					case 1:
						eval -= SMALL_TWO_COUNT;
						break;
					default:
						break;
				}
			}
		}
		return eval;
	}

	void PrintBoard(Board board)
	{
		string[] sqArr = {"nw", "n", "ne", "w", "c", "e", "sw", "s", "se"};
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
		Console.WriteLine("ZONE: " + (zone == -1 ? "ANY" : sqArr[zone]));
	}

	ValueTuple<int,List<int>> AlphaBeta(Board board,
		bool side, int depth, int alpha, int beta, List<int> prevLine)
	{
		List<int> moveList = MoveGen(board);
		if (moveList.Count() == 0)
		{
			int us, them;
			(us, them) = side ? (US, THEM) : (THEM, US);
			List<int> uLines = (from l in Lines(board.Item2) select l.Count(x => x == us)).ToList();
			List<int> tLines = (from l in Lines(board.Item2) select l.Count(x => x == them)).ToList();
			return ((uLines.Any(x => x == 3) ? OUTCOME_WIN + depth - SEARCHING_DEPTH :
				tLines.Any(x => x == 3) ? OUTCOME_LOSS - depth + SEARCHING_DEPTH : OUTCOME_DRAW), prevLine);
		}
		if (depth == 0)
			return (Evaluate(board, side), prevLine);
		List<int> pv = prevLine;
		int eval;
		List<int> line;
		foreach (int move in moveList)
		{
			(eval, line) = AlphaBeta(PlayMove(board, move, side), !side, depth-1, -beta, -alpha,
				prevLine.Concat(new List<int> {move}).ToList());
			eval = -eval;
			if (eval >= beta)
				return (beta, line);
			else if (eval > alpha || pv.Count() == 0)
			{
				alpha = eval;
				pv = line;
			}
		}
		return (alpha, pv);
	}

	string MoveString(int move)
	{
		string[] sqArr = {"nw", "n", "ne", "w", "c", "e", "sw", "s", "se"};
		return sqArr[move / 9] + "/" + sqArr[move % 9];
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
			new int[]{0, 0, 0, 0, 0, 0, 0, 0, 0},
		-1);
		bool player = true;
		PrintBoard(board);

		List<int> possibleMoves;
		string strMove, zone, square;
		string[] strSplit;

		int eval; List<int> line; List<string> strLines;
		int move;

		Stopwatch timer = new Stopwatch();

		if (selfStart)
		{
			timer.Start();
			(eval, line) = AlphaBeta(board, true, SEARCHING_DEPTH, -INFINITY, INFINITY, new List<int>());
			timer.Stop();
			board = PlayMove(board, line[0], true);
			strLines = (from mv in line select MoveString(mv)).ToList();
			Console.WriteLine("AI Move: " + strLines[0] + " PV: [" + string.Join(", ", strLines) + "] Eval: " + eval + " Time elapsed: " + timer.ElapsedMilliseconds + " ms");
			player = false;
		}

		PrintBoard(board);
		while (true)
		{
			possibleMoves = MoveGen(board);
			if (possibleMoves.Count() == 0)
			{
				Console.WriteLine("Game over");
				break;
			}
			Console.Write("Move: ");
			strMove = Console.ReadLine();
			strSplit = strMove.Split('/');
			zone = strSplit[0];
			square = strSplit[1];
			move = 9 * Array.FindIndex(sqArr, (x => x == zone))
				+ Array.FindIndex(sqArr, (x => x == square));
			while (!possibleMoves.Contains(move))
			{
				Console.Write("Move: ");
				strMove = Console.ReadLine();
				strSplit = strMove.Split('/');
				zone = strSplit[0];
				square = strSplit[1];
				move = 9 * Array.FindIndex(sqArr, (x => x == zone))
					+ Array.FindIndex(sqArr, (x => x == square));
			}
			board = PlayMove(board, move, player);
			PrintBoard(board);
			timer.Reset();
			timer.Start();
			(eval, line) = AlphaBeta(board, !player, SEARCHING_DEPTH, -INFINITY, INFINITY, new List<int>());
			timer.Stop();
			if (MoveGen(board).Count() == 0)
			{
				Console.WriteLine("Game over");
				break;
			}
			board = PlayMove(board, line[0], !player);
			strLines = (from mv in line select MoveString(mv)).ToList();
			Console.WriteLine("AI Move: " + strLines[0] + " PV: [" + string.Join(", ", strLines) + "] Eval: " + eval + " Time elapsed: " + timer.ElapsedMilliseconds + " ms");
			PrintBoard(board);
		}
	}
}

class Program
{
	public static void Main(string[] args)
	{
		UltimateTicTacToe uttt = new UltimateTicTacToe();
		uttt.Main(int.Parse(args[0]), args.Length == 1);
	}
}