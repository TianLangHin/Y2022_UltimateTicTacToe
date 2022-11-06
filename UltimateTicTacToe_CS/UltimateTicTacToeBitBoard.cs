using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;

// This program uses a bitboard-like representation to represent the state of the game.
using Board = System.ValueTuple<ulong,ulong,ulong>;

/*
The internal representation of Board is as follows:

Item1 (us)    : # {-SW-X--} {--E-X--} {--C-X--} {--W-X--} {-NE-X--} {--N-X--} {-NW-X--}
Bits used     : 1 <---9---> <---9---> <---9---> <---9---> <---9---> <---9---> <---9--->

Item2 (them)  : # {-SW-O--} {--E-O--} {--C-O--} {--W-O--} {-NE-O--} {--N-O--} {-NW-O--}
Bits used     : 1 <---9---> <---9---> <---9---> <---9---> <---9---> <---9---> <---9--->

Item3 (share) : ###### {ZN} {-LG-O--} {-LG-X--} {-SE-O--} {--S-O--} {-SE-W--} {--S-W--}
Bits used     : <-6--> <4-> <---9---> <---9---> <---9---> <---9---> <---9---> <---9--->

X refers to Player 1 ("X" in board display represents Player 1 occupancy)
O refers to Player 2 ("O" in board display represents Player 2 occupancy)

LG refers to Large Grid, which is the larger grid in which a three-in-a-row is made to win the game.

The abbreviations for the 9 relative positions within a grid are represented by:
+----+----+----+
| NW | N  | NE |
+----+----+----+
| W  | C  | E  |
+----+----+----+
| SW | S  | SE |
+----+----+----+
Their corresponding numerical values are NW = 0, N = 1, NE = 2, W = 3, C = 4, E = 5, SW = 6, S = 7, SE = 8.

ZN refers to zone in which the next player can play. It takes any of the above values, or the value ANY = 9.

# refers to unused bits.
*/

class UT3B2
{
	// Weights for representing win, loss and draw outcomes.
	const int INFINITY = 1000000;
	const int OUTCOME_WIN = INFINITY;
	const int OUTCOME_DRAW = 0;
	const int OUTCOME_LOSS = -INFINITY;

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
	const int ZONE_ANY = 9;

	// Masks for use in changing bitboards
	const ulong LINE        = 0b_111UL;
	const ulong CHUNK       = 0b_111_111_111UL;
	const ulong DBLCHUNK    = 0b_111_111_111_111_111_111UL;
	const ulong EXCLZONE    = 0b_111111_0000_111111111_111111111_111111111_111111111_111111111_111111111UL;
	const ulong CORNER_MASK = 0b_101_000_101UL;
	const ulong EDGE_MASK   = 0b_010_101_010UL;
	const ulong CENTRE_MASK = 0b_000_010_000UL;

	// Lookup table for population count, as only 9-bit integers will be using this table.
	int[] PopCount = new int[512];

	// Variable to contain the searching depth of the main AlphaBeta call.
	int SEARCHING_DEPTH;

	// Initialise PopCount lookup table by inserting bit count of each number
	// from 0 to 511 in the corresponding index in the array.
	public UT3B2()
	{
		for (int i = 0; i < 512; ++i)
			PopCount[i] = (i & 1) + ((i >> 1) & 1) + ((i >> 2) & 1) + ((i >> 3) & 1) +
				((i >> 4) & 1) + ((i >> 5) & 1) + ((i >> 6) & 1) + ((i >> 7) & 1) + ((i >> 8) & 1);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	ulong Lines(ulong grid)
	{
		// Returns an unsigned integer using LSH 24 bits in format:
		// 246 048 678 345 012 258 147 036
		// Where 0 = NW, 1 = N, 2 = NE, 3 = W, 4 = C, 5 = E, 6 = SW, 7 = S, 8 = SE.
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

	// The functions in this program all assume that we are starting with a valid board position.
	// Only valid positions will be reached if the program only ever uses its own functions to
	// play moves on the boards.

	// A move is represented as one integer, from 0 to 80 inclusive, representing one of the
	// 81 possible squares that a player can place their token on.
	List<int> GenerateMoves(Board board)
	{
		(ulong us, ulong them, ulong share) = board;

		int i; // variable counter to be used potentially in multiple for loops in one pass

		// The variables *data1* and *data2* are reused to save memory.
		// They serve different purposes in different parts of this function.

		ulong data1 = Lines((share >> 36) & CHUNK); // Contains all big grid lines for X
		ulong data2 = Lines((share >> 45) & CHUNK); // Contains all big grid lines for O

		// If either X or O has made a big grid three-in-a-row, the game is over.
		for (i = 0; i < 24; i += 3)
			if (((data1 >> i) & LINE) == LINE || ((data2 >> i) & LINE) == LINE)
				return new List<int>();

		List<int> moveList = new List<int>(); // List for all legal moves, to be returned.

		// Now, the variables *data1* and *data2* are being reused.

		data1 = us | them;                           // Contains all occupancies in zones NW to SW
		data2 = ((share >> 18) | share) & DBLCHUNK;  // Contains all occupancies in zones S to SE
		ulong large = ((share >> 36) | (share >> 45)) & CHUNK; // Contains all occupancies in big grid

		int zone = (int)((share >> 54) & 0b1111); // Determine zone the player is allowed to play in

		switch (zone)
		{
			// If the player is allowed to play in any zone they wish, select all blank squares
			// that are not in a zone that has a corresponding occupied large grid.
			case ZONE_ANY:
				// The iteration over all squares has to be done separately due to the Board format.
				for (i = 0; i < 63; ++i)
					if (((data1 >> i) & 1) == 0 && ((large >> (i/9)) & 1) == 0)
						moveList.Add(i);
				for (; i < 81; ++i)
					if (((data2 >> (i-63)) & 1) == 0 && ((large >> (i/9)) & 1) == 0)
						moveList.Add(i);
				break;
			// In the following, we will assume that the *zone* value given corresponds to a zone that
			// does not correspond to an occupied space in the large grid.

			// Zones S and SE have to be dealt with separately due to the Board format.
			case 7: case 8:
				zone *= 9; // Since only the value of 9*zone will be used from here on, update *zone* itself.
				// Bitshift right to make relevant zone equal to the LSB 9 bits.
				data2 >>= zone - 63; // Since we are accessing *share*, we bishift right by 63 less.
				for (i = 0; i < 9; ++i)
					if (((data2 >> i) & 1) == 0)
						moveList.Add(zone + i);
				break;
			default:
				zone *= 9; // Since only the value of 9*zone will be used from here on, update *zone* itself.
				data1 >>= zone; // Bitshift right to make relevant zone equal to the LSB 9 bits.
				for (i = 0; i < 9; ++i)
					if (((data1 >> i) & 1) == 0)
						moveList.Add(zone + i);
				break;
		}

		return moveList; // Return list of legal moves.
	}

	// Returns the new board state after the given move has been played by the given side.
	// This will not affect the value of the board passed into this function.
	Board PlayMove(Board board, int move, int side) // side = 0 if player is X, 1 if player is O
	{
		(ulong us, ulong them, ulong share) = board;

		ulong lines;     // This will contain our line occupancies of the zone we place in this move.
		ulong nextChunk; // This will contain all occupancies of the next zone to play in.

		// If in the zone S or SE, we update *share*. Otherwise, update *us* or *them*.
		if (move > 62)
		{
			// The relevant position is the square number - 63, then 18 places further if O is playing.
			share |= 1UL << (move - 63 + 18*side);
			// Relevant zone is INT(move / 9), so we bitshift by 9*zone - 63 to get correct zone,
			// once again offsetting by 18 if player O is playing.
			lines = Lines((share >> (9*(move / 9) - 63 + 18*side)) & CHUNK);
		}
		else if (side == 0) // Player X is playing.
		{
			us |= 1UL << move; // Update relevant position.
			lines = Lines((us >> (9*(move / 9))) & CHUNK); // Find lines in relevant zone for this move.
		}
		else // Player O is playing.
		{
			them |= 1UL << move; // Update relevant position.
			lines = Lines((them >> (9*(move / 9))) & CHUNK); // Find lines in relevant zone for this move.
		}

		// To locate the occupancies for the zone corresponding to the next move,
		// whether the zone will be S to SE or otherwise needs to be considered,
		// due to the different locations of these zones in the Board representation.
		nextChunk = move % 9 > 6 ?
			((share | (share >> 18)) >> (9*((move % 9) - 7))) & CHUNK : // overlap both players in *share*
			((us | them) >> (9*(move % 9))) & CHUNK; // combine both player occupations from *us* and *them*

		// For the lines in the zone we just filled, check to see if we have made a three-in-a-row.
		// This is the only thing we need to check as we are assuming that we are starting from
		// a valid position the whole time, and that the functions here are the only ones that
		// have edited the value of the board.
		bool lineNotWon = true;
		for (int i = 0; i < 24 && lineNotWon; i += 3)
			if (((lines >> i) & LINE) == LINE) // The LSB 3 bits represents one of the eight lines in the zone.
				lineNotWon = false;

		// Change large grid contents by updating the correct section of *share*.
		// The correct bit position is 36 bits from LSB, then 0 to 8 positions left depending on move,
		// then shifted another 9 spaces if the line is made by player O.
		if (!lineNotWon)
			share |= 1UL << (36 + 9*side + move/9);

		// Normally, the zone of the next move is determined by move % 9.
		// However, if that corresponding zone is completely filled, or
		// the large occupancy corresponding to the zone is occupied, then the player can move anywhere.
		int zone = nextChunk == CHUNK || (((share | (share >> 9)) >> (36 + move % 9)) & 1) == 1 ? ZONE_ANY : move % 9;

		share = (share & EXCLZONE) | ((ulong)zone << 54); // Put *zone* value into correct place in *share*.

		return (us, them, share); // Return board with new updated values.
	}

	// Heuristic for evaluating a particular board from the perspective of a particular side,
	// once the minimax reaches a depth = 0 or other leaf node.

	// There are two components to this evaluation: line scoring and positional scoring.
	// Line scoring gives a score for each line in the large grid and each of the smaller ones,
	// scoring based on how many occupancies have been made by the player. A line is given no score
	// if no three-in-a-row can be possible for the player.
	// Positional scoring gives a score based purely on the position of the occupancy in the grid,
	// that is, giving a score for a corner, edge or centre occupancy.
	int Evaluate(Board board, int side)
	{
		(ulong us, ulong them, ulong share) = board;

		// Throughout the function, the evaluation is calculated from the perspective of X.
		// It is then flipped according to *side* at the end.

		int eval = 0,         // Variable to store incrementally added evaluation
			i, j,             // Counter variables, which will be reused
			lineUs, lineThem; // Stores the count of three-in-a-rows from player X and O respectively

		// Line scoring begins here.

		// These variables are reused throughout the processes of this function.
		ulong usData = Lines((share >> 36) & CHUNK);   // Stores the X occupancies in the large grid
		ulong themData = Lines((share >> 45) & CHUNK); // Stores the O occupancies in the large grid

		// Stores combined occupancies in the large grid.
		ulong large = ((share >> 36) | (share >> 45)) & CHUNK;

		// First, we check the large grid.
		// Increment *i* by 3 each time to extract the LSB 3 bits each time.
		for (i = 0; i < 24; i += 3)
		{
			lineUs = PopCount[(usData >> i) & LINE];     // Count X occupancies in this line
			lineThem = PopCount[(themData >> i) & LINE]; // Count O occupancies in this line

			// If both players have occupied this line, neither can form a three-in-a-row in this line.
			if (lineUs != 0 && lineThem != 0)
				continue;

			// In the following, we do not check for three-in-a-rows, because they are checked when there are
			// no legal moves, and the state of three-in-a-rows is checked outside this function
			// in the processing of leaf nodes in the AlphaBeta function.

			// Checks number of X occupancies.
			switch (lineUs)
			{
				case 2:
					eval += BIG_TWO_COUNT; // Add weight to evaluation
					break;
				case 1:
					eval += BIG_ONE_COUNT; // Add weight to evaluation
					break;
				default:
					break;
			}

			// Checks number of O occupancies.
			switch (lineThem)
			{
				case 2:
					eval -= BIG_TWO_COUNT; // Due to symmetry, subtract same weight from evaluation
					break;
				case 1:
					eval -= BIG_ONE_COUNT; // Subtract weight from evaluation
					break;
				default:
					break;
			}
		}

		// If the large grid has been completely filled, and yet no three-in-a-rows have been detected earlier,
		// then the game is a draw.
		if (large == CHUNK)
			return OUTCOME_DRAW;

		// Now, we loop through each of the 9 zones.
		for (i = 0; i < 9; ++i)
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

			usData = Lines(usData);     // Stores all lines X makes in this zone
			themData = Lines(themData); // Stores all lines O makes in this zone

			// Loop through each line, incrementing *j* by 3 each time to make relevant line LSB 3 bits.
			for (j = 0; j < 24; j += 3)
			{
				lineUs = PopCount[(usData >> j) & LINE];     // Count X occupancies in this line
				lineThem = PopCount[(themData >> j) & LINE]; // Count O occupancies in this line

				// If both players have occupied this line, neither can score using this line.
				// Hence we do not score it.
				if (lineUs != 0 && lineThem != 0)
					continue;

				// Checks number of X occupancies.
				switch (lineUs)
				{
					// Since we assume that the board is valid, any three-in-a-rows for X
					// must already have been detected as a filled large grid occupancy.
					case 2:
						eval += SMALL_TWO_COUNT; // Add weight to evaluation
						break;
					case 1:
						eval += SMALL_ONE_COUNT; // Add weight to evaluation
						break;
					default:
						break;
				}

				// Checks number of O occupancies.
				switch (lineThem)
				{
					// Similarly, we assume all three-in-a-rows for O must already have been detected.
					case 2:
						eval -= SMALL_TWO_COUNT; // Due to symmetry, subtract same weight from evaluation
						break;
					case 1:
						eval -= SMALL_ONE_COUNT; // Subtract weight from evaluation
						break;
					default:
						break;
				}
			}
		}

		// Line scoring ends here, and positional scoring begins here.

		// Counts the number of corners, edges and centres are occupied.
		// Each variable is incremented for an X occupancy, and decremented for an O occupancy
		// so that opposing occupancies cancel each other out.
		int corner = 0, edge = 0, centre = 0;

		// Use CORNER_MASK, EDGE_MASK and CENTRE_MASK to include only the relevant positions
		// in a grid, and then PopCount lookup to count how many occupancies there are.
		// Increment *i* by 9 each time to make LSB 9 bits the relevant zone.

		// Deal with zones NW to SW first, due to data representation.
		for (i = 0; i < 63; i += 9)
		{
			corner += PopCount[(us >> i) & CORNER_MASK] - PopCount[(them >> i) & CORNER_MASK];
			edge   += PopCount[(us >> i) & EDGE_MASK]   - PopCount[(them >> i) & EDGE_MASK];
			centre += PopCount[(us >> i) & CENTRE_MASK] - PopCount[(them >> i) & CENTRE_MASK];
		}
		// Then deal with zones S to SE.
		for (i = 0; i < 18; i += 9)
		{
			corner += PopCount[(share >> i) & CORNER_MASK] - PopCount[(share >> (i + 18)) & CORNER_MASK];
			edge   += PopCount[(share >> i) & EDGE_MASK]   - PopCount[(share >> (i + 18)) & EDGE_MASK];
			centre += PopCount[(share >> i) & CENTRE_MASK] - PopCount[(share >> (i + 18)) & CENTRE_MASK];
		}

		// Multiply each type of position by its corresponding weight, multiplying another weight
		// for the positional occupancies found in the large grid.
		eval +=
			CORNER * (corner + SQ_BIG * (PopCount[(share >> 36) & CORNER_MASK] - PopCount[(share >> 45) & CORNER_MASK])) +
			EDGE   * (edge   + SQ_BIG * (PopCount[(share >> 36) & EDGE_MASK]   - PopCount[(share >> 45) & EDGE_MASK]))   +
			CENTRE * (centre + SQ_BIG * (PopCount[(share >> 36) & CENTRE_MASK] - PopCount[(share >> 45) & CENTRE_MASK]));

		// Due to not being colour agnostic, we need to multiply eval by -1 if side = 1, otherwise 1 if side = 0.
		// Instead of using a condition, (1 - 2 * side) gives the desired values as stated above.
		// Replacing the multiplication with a suitable bitshift, we have the below expression.
		return eval * (1 - (side << 1));
	}

	// The main alpha-beta minimax function. Returns evaluation and principal variation.
	ValueTuple<int,int[]> AlphaBeta(Board board, int side, int depth, int alpha, int beta)
	{
		List<int> moveList = GenerateMoves(board); // Find all legal moves.

		if (moveList.Count() == 0) // If there are no legal moves, the game is over.
		{
			ulong usLines = Lines((board.Item3 >> 36) & CHUNK);   // Find X large grid occupancies
			ulong themLines = Lines((board.Item3 >> 45) & CHUNK); // Find O large grid occupancies
			// Switch perspective if needed
			if (side == 1)
			{
				ulong temp = usLines;
				usLines = themLines;
				themLines = temp;
			}
			// Find any three-in-a-rows for both players
			bool usNotWin = true, themNotWin = true;
			for (int i = 0; i < 24 && usNotWin && themNotWin; i += 3)
			{
				if (((usLines >> i) & LINE) == LINE)
					usNotWin = false;
				if (((themLines >> i) & LINE) == LINE)
					themNotWin = false;
			}
			return (
				// Player is winning, so subtract the number of moves needed to reach the win.
				// This is to help find the fastest way to win.
				(!usNotWin ? OUTCOME_WIN + depth - SEARCHING_DEPTH :
				// Player is losing, so subtract the number of moves need to reach the end.
				// This is to help delay the loss as much as possible.
				!themNotWin ? OUTCOME_LOSS - depth + SEARCHING_DEPTH :
				// If neither has a three-in-a-row but the game is over, then it is a draw.
				OUTCOME_DRAW),
				new int[SEARCHING_DEPTH - depth]); // Reached leaf node, so return empty PV.
		}

		// Reached leaf node, so return static evaluation and empty PV.
		if (depth == 0)
			return (Evaluate(board, side), new int[SEARCHING_DEPTH]);

		int eval, length = moveList.Count();
		int[] pv = new int[SEARCHING_DEPTH], line;

		// Iterate over all legal moves.
		for (int i = 0; i < length; ++i)
		{
			// Recursive minimax call, using negamax construct as evaluation is symmetric.
			(eval, line) = AlphaBeta(PlayMove(board, moveList[i], side), ~side & 1, depth-1, -beta, -alpha);
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

	void PrintBoard(Board board)
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
				Console.WriteLine(string.Join("|",
					(
						from grid in bigRow select
						string.Join("", (from smallRow in new ArraySegment<int>(grid, j, 3) select smallRow == US ? "X" : smallRow == THEM ? "O" : "."))
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

	// First part is the zone, second part is the spot within that zone's grid.
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

	// The main game loop to be executed.
	public void Main(int searchingDepth, bool selfStart)
	{
		SEARCHING_DEPTH = searchingDepth;
		string[] sqArr = {"nw", "n", "ne", "w", "c", "e", "sw", "s", "se"};

		// Starting values for an empty board. The zone is ZONE_ANY, as the first player can place anywhere.
		Board board = (
			0b0_000000000_000000000_000000000_000000000_000000000_000000000_000000000UL,
			0b0_000000000_000000000_000000000_000000000_000000000_000000000_000000000UL,
			0b000000_1001_000000000_000000000_000000000_000000000_000000000_000000000UL
		);

		int player = 0; // Player is playing X by default.
		PrintBoard(board); // Print the starting position.

		List<int> possibleMoves;
		string strMove, zone, square;
		string[] strSplit;

		int eval, move;
		int[] line;
		List<string> strLines;

		Stopwatch timer = new Stopwatch(); // The timer will show how many milliseconds each evaluation takes.

		if (selfStart) // *selfStart* is true if the program is to play the first move. The player is thus playing O.
		{
			// Output evaluation, PV and new board state.
			timer.Start();
			(eval, line) = AlphaBeta(board, 0, SEARCHING_DEPTH, -INFINITY, INFINITY);
			timer.Stop();
			board = PlayMove(board, line[0], 0);
			strLines = (from mv in line select MoveString(mv)).ToList();
			Console.WriteLine("AI Move: " + strLines[0] + " PV: [" + string.Join(", ", strLines) + "] Eval: " + EvalString(eval) + " Time elapsed: " + timer.ElapsedMilliseconds + " ms");
			player = 1; // Player is playing O.
		}

		PrintBoard(board);
		while (true)
		{
			possibleMoves = GenerateMoves(board);
			if (possibleMoves.Count() == 0)
			{
				Console.WriteLine("Game over");
				Console.ReadLine();
				break;
			}

			// Move format is "{direction}/{direction}", where {direction} is one of: "nw", "n", "ne", "w", "c", "e", "sw", "s", "se".
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
			move = 9 * Array.FindIndex(sqArr, (x => x == zone)) + Array.FindIndex(sqArr, (x => x == square)); // Convert string to integer move representation

			while (!possibleMoves.Contains(move)) // Prompt for move until inputted move is legal
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
				move = 9 * Array.FindIndex(sqArr, (x => x == zone)) + Array.FindIndex(sqArr, (x => x == square));
			}

			// Play the inputted (and validated) move.
			board = PlayMove(board, move, player);
			PrintBoard(board);
			if (GenerateMoves(board).Count() == 0)
			{
				Console.WriteLine("Game over");
				Console.ReadLine();
				break;
			}

			// Output evaluation, PV and new board state.
			timer.Reset();
			timer.Start();
			(eval, line) = AlphaBeta(board, ~player & 1, SEARCHING_DEPTH, -INFINITY, INFINITY);
			timer.Stop();
			board = PlayMove(board, line[0], ~player & 1);
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
		UT3B2 ut3b2 = new UT3B2();

		// Input the depth for this program to work at.
		int depth;
		Console.Write("Depth: ");
		while (!int.TryParse(Console.ReadLine(), out depth))
			Console.Write("Depth: ");

		// Press '1' for program to play first ("X"), press '2' for program to play second ("O")
		char c;
		while ((c = Console.ReadKey(true).KeyChar) != '1' && c != '2') {}
		Console.WriteLine("Playing " + (c == '1' ? "X" : "O"));
		ut3b2.Main(depth, c == '1');
	}
}