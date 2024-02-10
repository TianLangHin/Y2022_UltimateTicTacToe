/**
 * The bitboard structure is represented here as a tuple of 3 `u64`s.
 * Though the elements do not have inherent names, all elements
 * are always destructured with the `us`, `them` and `share` names.
 */
pub type Board = (u64, u64, u64);
pub type Move = u64;

// Weights for representing win, loss and draw outcomes.
pub const OUTCOME_WIN: i32 = 1000000;
pub const OUTCOME_DRAW: i32 = 0;
pub const OUTCOME_LOSS: i32 = -1000000;

// Weights for line scoring.
const BIG_TWO_COUNT: i32 = 90;
const BIG_ONE_COUNT: i32 = 20;
const SMALL_TWO_COUNT: i32 = 8;
const SMALL_ONE_COUNT: i32 = 1;

// Weights for positional scoring.
const CENTRE: i32 = 9;
const CORNER: i32 = 7;
const EDGE: i32 = 5;
const SQ_BIG: i32 = 25;

// Masks for use in changing bitboards.
const LINE: u64 = 0b111;
const CHUNK: u64 = 0b111111111;
const DBLCHUNK: u64 = (CHUNK << 9) | CHUNK;
const EXCLZONE: u64 = !(0b1111u64 << 54);
const CORNER_MASK: u64 = 0b_101_000_101;
const EDGE_MASK: u64 = 0b_010_101_010;
const CENTRE_MASK: u64 = 0b_000_010_000;

// Internal representation for `zone` value, outside of the 0-8 range,
// to indicate that the player can play in any zone.
pub const ZONE_ANY: u64 = 9;

// Arrays to readily convert integers in the 0-8 range to the
// name of their corresponding zone.
pub const ZONE_ARRAY_UPPER: [&str; 9] = ["NW", "N", "NE", "W", "C", "E", "SW", "S", "SE"];
pub const ZONE_ARRAY_LOWER: [&str; 9] = ["nw", "n", "ne", "w", "c", "e", "sw", "s", "se"];

// Since values 0-80 are best used for move representation,
// 81 is used to represent a "null move".
pub const NULL_MOVE: Move = 81;

// The absolute upper bound of total plies for this game is 81,
// since there are exactly 81 places that can be played.
pub const MAX_PLY: usize = 81;

/**
 * Due to the potential unreadability of an if-block in an arithmetic expression,
 * the `toggle_shift` and `toggle_eval` functions provide functions to adjust
 * a number based on a `bool` flag.
 */

#[inline]
const fn toggle_shift(side: bool, num: u64) -> u64 {
    // Returns either the given `u64` or `0`.
    if side {
        num
    } else {
        0
    }
}

#[inline]
const fn toggle_eval(side: bool, num: i32) -> i32 {
    // Returns either the given `i32` or the negative of it.
    if side {
        -num
    } else {
        num
    }
}

/**
 * A grid is represented by the least significant 9 bits in a `u64`.
 * The lines in a grid are represented by the following combinations of zones:
 * NW-N-NE, W-C-E, SW-S-SE, NW-W-SW, N-C-S, NE-E-SE, NW-C-SE, NE-C-SW.
 */

// Returns a 24-bit value where each set of 3 bits
// represents the occupancy pattern of that line.
// A 1 bit means that particular position in that line is occupied.
#[inline]
pub const fn lines(grid: u64) -> u64 {
    0b_000_100_000_000_100_000_000_100 * (grid & 1)
        + 0b_000_000_000_000_010_000_100_000 * ((grid >> 1) & 1)
        + 0b_100_000_000_000_001_100_000_000 * ((grid >> 2) & 1)
        + 0b_000_000_000_100_000_000_000_010 * ((grid >> 3) & 1)
        + 0b_010_010_000_010_000_000_010_000 * ((grid >> 4) & 1)
        + 0b_000_000_000_001_000_010_000_000 * ((grid >> 5) & 1)
        + 0b_001_000_100_000_000_000_000_001 * ((grid >> 6) & 1)
        + 0b_000_000_010_000_000_000_001_000 * ((grid >> 7) & 1)
        + 0b_000_001_001_000_000_001_000_000 * ((grid >> 8) & 1)
}

// Returns an 8-bit value that represents
// the occupancy status of each line in a 3x3 grid
// where a 1 bit means that line is formed.
#[inline]
pub const fn line_presence(grid: u64) -> bool {
    0 != ((0b10110110 | ((grid & 1) * 0xff))
        & (0b11101110 | (((grid >> 1) & 1) * 0xff))
        & (0b01011110 | (((grid >> 2) & 1) * 0xff))
        & (0b11110101 | (((grid >> 3) & 1) * 0xff))
        & (0b00101101 | (((grid >> 4) & 1) * 0xff))
        & (0b11011101 | (((grid >> 5) & 1) * 0xff))
        & (0b01110011 | (((grid >> 6) & 1) * 0xff))
        & (0b11101011 | (((grid >> 7) & 1) * 0xff))
        & (0b10011011 | (((grid >> 8) & 1) * 0xff)))
}

/**
 * This function is to be executed at the very start, and only once,
 * to populate the lookup tables to be used in the heuristic evaluation.
 * Since each of the two lookup tables contain 262144 `i32` values,
 * Vec<i32> is returned instead of an array, to avoid stack overflow.
 */
pub fn init() -> (Vec<i32>, Vec<i32>) {
    // These lookup tables store evaluations for different arrangements of grids,
    // for both small and large grid metrics.
    // These tables will essentially store partial heuristic evaluations
    // for all possible small grid arrangements in the game.

    // These values are calculated from the perspective of player X,
    // so will have to be negated for player O,
    // as this program uses a symmetrical heuristic.

    let mut eval_table_large: Vec<i32> = vec![0; 262144];
    let mut eval_table_small: Vec<i32> = vec![0; 262144];

    // For each integer from 0 to 511, record the number of 1 bits it has.
    let pop_count: Vec<i32> = (0..512)
        .map(|i| (0..9).fold(0, |acc, j| acc + ((i >> j) & 1)))
        .collect();

    // We test all the possible arrangements, which is where
    // `us` and `them` each take a value from 0 to 511 each.
    for us in (0..512).map(|us| us as u64) {
        for them in (0..512).map(|them| them as u64) {
            // These evaluation values will be incrementally updated.
            let mut eval_large: i32 = 0;
            let mut eval_small: i32 = 0;

            // Retrieve the lines that each side makes as a bit array,
            // allowing the number of occupancies in each line to be found.
            let us_lines = lines(us);
            let them_lines = lines(them);

            // Early escape boolean flags, since no more evaluation is needed
            // if one particular side has made a 3-in-a-row.
            let mut us_won: bool = false;
            let mut them_won: bool = false;

            // We process the bits returned from `lines` in groups of 3.
            for i in (0..24).step_by(3) {
                // Count how many cells each side occupies in this line.
                let us_count = pop_count[((us_lines >> i) & LINE) as usize];
                let them_count = pop_count[((them_lines >> i) & LINE) as usize];

                // If both sides already occupy a place in this line,
                // this line is no longer winnable for either side.
                if us_count != 0 && them_count != 0 {
                    continue;
                }
                // Player X has won a line: X wins this configuration already.
                if us_count == 3 {
                    us_won = true;
                    break;
                }
                // Player O has won a line: O wins this configuration already.
                if them_count == 3 {
                    them_won = true;
                    break;
                }

                // Add on scores for occupying more of a line for both sides.

                eval_large += match us_count {
                    2 => BIG_TWO_COUNT,
                    1 => BIG_ONE_COUNT,
                    _ => 0,
                } - match them_count {
                    2 => BIG_TWO_COUNT,
                    1 => BIG_ONE_COUNT,
                    _ => 0,
                };
                eval_small += match us_count {
                    2 => SMALL_TWO_COUNT,
                    1 => SMALL_ONE_COUNT,
                    _ => 0,
                } - match them_count {
                    2 => SMALL_TWO_COUNT,
                    1 => SMALL_ONE_COUNT,
                    _ => 0,
                };
            }

            // Add on scores for occupancies in certain positions.
            let eval_pos = CORNER
                * (pop_count[(us & CORNER_MASK) as usize]
                    - pop_count[(them & CORNER_MASK) as usize])
                + EDGE
                    * (pop_count[(us & EDGE_MASK) as usize]
                        - pop_count[(them & EDGE_MASK) as usize])
                + CENTRE
                    * (pop_count[(us & CENTRE_MASK) as usize]
                        - pop_count[(them & CENTRE_MASK) as usize]);

            // Update large table with evaluation if a decisive result is reached,
            // otherwise update both small and large table with suitable heuristics.
            if us_won {
                eval_table_large[((them << 9) | us) as usize] = OUTCOME_WIN;
            } else if them_won {
                eval_table_large[((them << 9) | us) as usize] = OUTCOME_LOSS;
            } else if pop_count[(us | them) as usize] == 9 {
                eval_table_large[((them << 9) | us) as usize] = OUTCOME_DRAW;
            } else {
                eval_table_large[((them << 9) | us) as usize] = eval_large + eval_pos * SQ_BIG;
                eval_table_small[((them << 9) | us) as usize] = eval_small + eval_pos;
            }
        }
    }

    // Implicit return.
    (eval_table_large, eval_table_small)
}

/**
 * The functions below all assume that we are starting with a valid board position.
 * Only valid positions will be reached if the program only ever uses its own functions
 * to play moves on the boards.
 */

// To avoid running `.collect()` once every time a move list is generated
// only to be iterated over again in the functions it is used in,
// this function instead returns an iterator trait object.
pub fn generate_moves(board: Board) -> impl Iterator<Item = Move> {

    // This function uses an enum to wrap all the iterators returned
    // and implements Iterator for it, exactly how auto_enums would.
    enum LegalMoves<_T1, _T2, _T3> {
        NoMoves,
        AllZones(_T1),
        FirstSeven(_T2),
        LastTwo(_T3),
    }

    impl<_T1, _T2, _T3> Iterator for LegalMoves<_T1, _T2, _T3>
    where
        _T1: Iterator,
        _T2: Iterator<Item = <_T1 as Iterator>::Item>,
        _T3: Iterator<Item = <_T1 as Iterator>::Item>,
    {
        type Item = <_T1 as Iterator>::Item;

        #[inline]
        fn next(&mut self) -> Option<Self::Item> {
            match self {
                Self::NoMoves => None,
                Self::AllZones(x) => x.next(),
                Self::FirstSeven(x) => x.next(),
                Self::LastTwo(x) => x.next(),
            }
        }

        #[inline]
        fn size_hint(&self) -> (usize, Option<usize>) {
            match self {
                Self::NoMoves => (0, Some(0)),
                Self::AllZones(x) => x.size_hint(),
                Self::FirstSeven(x) => x.size_hint(),
                Self::LastTwo(x) => x.size_hint(),
            }
        }
    }

    let (us, them, share) = board;

    if line_presence(share >> 36) || line_presence(share >> 45) {
        return LegalMoves::NoMoves;
    }

    // Extract the zone to be played from the board.
    let zone = (share >> 54) & 0b1111;

    // The moves themselves correspond to actual integers that access the bitboards directly,
    // hence we avoid using `map`, instead only using `filter` and `chain`.
    match zone {
        // If the player is allowed to play in any zone they wish, select all blank squares
        // that are not in a zone that has a corresponding occupied large grid.
        ZONE_ANY => {
            let nw_to_sw = us | them;
            let s_to_se = (share >> 18) | share;
            let large = (share >> 36) | (share >> 45);

            LegalMoves::AllZones(
                (0..63)
                    .filter(move |i| ((nw_to_sw >> i) & 1) == 0 && ((large >> (i / 9)) & 1) == 0)
                    .chain((63..81).filter(move |i| {
                        ((s_to_se >> (i - 63)) & 1) == 0 && ((large >> (i / 9)) & 1) == 0
                    })),
            )
        }

        // For zones S and SE, we access `share`.
        7 | 8 => {
            let s_to_se = (share >> 18) | share;
            LegalMoves::LastTwo(
                (9 * zone..9 * zone + 9).filter(move |i| ((s_to_se >> (i - 63)) & 1) == 0),
            )
        }

        // For zones NW to SW, we access `us` and `them`.
        _ => {
            let nw_to_sw = us | them;
            LegalMoves::FirstSeven(
                (9 * zone..9 * zone + 9).filter(move |i| ((nw_to_sw >> i) & 1) == 0),
            )
        }
    }
}

/**
 * For a given move played by a given player, returs the new board state.
 * Since Board is a tuple of primitive types, copies should be cheap enough,
 * eliminating the desire to construct a function that mutates the passed Board.
 */
pub fn play_move(board: Board, mv: Move, side: bool) -> Board {
    // Each move makes an incremental change to the board,
    // so we create mutable copies of the `u64` components of the board.
    let (mut us, mut them, mut share) = board;

    // `line_occupancy` stores whether a line within the relevant zone is formed by us
    // after this move is made.

    let line_occupancy = if mv > 62 {
        share |= 1 << (mv - 63 + toggle_shift(side, 18));

        // implicit return in block
        line_presence(share >> (9 * (mv / 9) - 63 + toggle_shift(side, 18)))
    } else if !side {
        us |= 1 << mv;

        // implicit return in block
        line_presence(us >> (9 * (mv / 9)))
    } else {
        them |= 1 << mv;

        // implicit return in block
        line_presence(them >> (9 * (mv / 9)))
    };

    // If this move forms a line in our zone, occupy the corresponding large grid.
    if line_occupancy {
        share |= 1 << (36 + toggle_shift(side, 9) + mv / 9);
    }

    // `next_chunk` contains all occupancies of the zone we are moving next in.
    // The next zone to be played in is determined by the position of the current move
    // relative to other cells in its zone, found by `mv % 9`.
    // This determines if we access `share` or `us` and `them`.

    let next_chunk = if mv % 9 > 6 {
        ((share | (share >> 18)) >> (9 * ((mv % 9) - 7))) & CHUNK
    } else {
        ((us | them) >> (9 * (mv % 9))) & CHUNK
    };

    // The next player is allowed to play in any zone if either:
    // the zone indicated by the most recent move corresponds to a large grid that is won,
    // or the zone is completely filled with zero vacant cells.

    let zone = if next_chunk == CHUNK || (((share | (share >> 9)) >> (36 + mv % 9)) & 1) == 1 {
        ZONE_ANY
    } else {
        mv % 9
    };

    // We overwrite the bits in `share` completely with the new value of `zone`.
    (us, them, (share & EXCLZONE) | (zone << 54))
}

/**
 * Heuristic for evaluating a particular board state for a given side.
 * This function uses the precomputed values from `init()`,
 * passed as a reference in its parameter.
 */
pub fn evaluate(board: Board, side: bool, tables: &(Vec<i32>, Vec<i32>)) -> i32 {
    let (us, them, share) = board;

    // First, check the evaluation of the large grid.
    let eval = tables.0[((share >> 36) & DBLCHUNK) as usize];

    // If the large grid has reached a decisive result, the game is over,
    // with either a win or loss depending on the side currently evaluating this position.
    if eval == OUTCOME_WIN || eval == OUTCOME_LOSS {
        return toggle_eval(side, eval);
    }

    // If the large grid does not have a won line,
    // but is completely filled, the game is a draw.
    let large = ((share >> 36) | (share >> 45)) & CHUNK;
    if large == CHUNK {
        return OUTCOME_DRAW;
    }

    // Due to the different components that the zones NW to SW and S to SE are stored,
    // we once again chain two iterators together to prevent having to check
    // the condition each time.

    // We use `toggle_eval` to adjust the evaluation for the side we are evaluating for,
    // thus we pass the entire expression to the `toggle_eval` function.
    toggle_eval(
        side,
        (0..7)
            .map(|i| {
                let us_data = (us >> (9 * i)) & CHUNK;
                let them_data = (them >> (9 * i)) & CHUNK;

                // Zones that are comlpetely filled or correspond to an occupied large grid
                // are not scored. Since the values are added,
                // we return a zero for this situation.
                if ((large >> i) & 1) == 1 || (us_data | them_data) == CHUNK {
                    0
                } else {
                    // Incrementally add the precomputed evaluation of the small grid.
                    tables.1[((them_data << 9) | us_data) as usize]
                }
            })
            .chain((7..9).map(|i| {
                let us_data = (share >> (9 * i - 63)) & CHUNK;
                let them_data = (share >> (9 * i - 45)) & CHUNK;

                if ((large >> i) & 1) == 1 || (us_data | them_data) == CHUNK {
                    0
                } else {
                    tables.1[((them_data << 9) | us_data) as usize]
                }
            }))
            .fold(eval, |acc, x| acc + x),
        // Finally, add the elements up on top of the scoring for the large grid.
    )
    // The above implicit returns.
}

/**
 * The main alpha-beta minimax function.
 * Uses a negamax construct since the heuristic is symmetric.
 * Returns evaluation and the principal variation.
 */
pub fn alpha_beta(
    board: Board,
    side: bool,
    depth: usize,
    mut alpha: i32, // The `alpha` variable will be updated throughout, and is cheaply copied.
    beta: i32,
    tables: &(Vec<i32>, Vec<i32>),
    max_depth: usize,
) -> (i32, [u64; MAX_PLY]) {
    // It is not always necessary to destructure the board,
    // as only one branch of this function uses one of the components.
    // The board is otherwise passed as is.

    // Leaf node returns static evaluation and empty PV.
    if depth == 0 {
        return (evaluate(board, side, tables), [NULL_MOVE; MAX_PLY]);
    }

    // Retrieve the iterator for move generation.
    let mut move_list = generate_moves(board);

    // Retrieve first element into mutable binding,
    // branching immediately if `None` first (i.e. empty iterator)
    if let Some(mut mv) = move_list.next() {
        // Initialise PV array that will be updated over iterations.
        let mut pv = [NULL_MOVE; MAX_PLY];

        // Equivalent to do-while loop.
        loop {
            // Recursive alpha-beta call
            let (mut eval, mut line) = alpha_beta(
                play_move(board, mv, side),
                !side,
                depth - 1,
                -beta,
                -alpha,
                tables,
                max_depth,
            );

            // Take the negative of the evaluation to adjust for our current side.
            eval = -eval;

            // Record this move in the line.
            line[max_depth - depth] = mv;

            if eval >= beta {
                // Fail-hard beta cutoff.
                return (beta, line);
            } else if eval > alpha {
                // New best move found. Update PV.
                alpha = eval;
                pv = line;
            }

            // Break out of loop if next move is None, update `mv` binding otherwise.
            if let Some(new_mv) = move_list.next() {
                mv = new_mv;
            } else {
                break;
            }
        }
        // implicit return
        (alpha, pv)
    } else {
        // If the very first retrieval was a `None`,
        // this position has no legal moves, and thus the game is over.

        // We need only to check the evaluation of the large grid.
        let eval = toggle_eval(side, tables.0[((board.2 >> 36) & DBLCHUNK) as usize]);

        // If the outcome is decisive (win or lose), we scale it inwards
        // by the number of plies it will take to reach the conclusion.
        match eval {
            OUTCOME_WIN => (eval - (max_depth - depth) as i32, [NULL_MOVE; MAX_PLY]),
            OUTCOME_LOSS => (eval + (max_depth - depth) as i32, [NULL_MOVE; MAX_PLY]),
            // If the large grid does not give a win or loss
            // but there are no legal moves, the game is drawn.
            _ => (OUTCOME_DRAW, [NULL_MOVE; MAX_PLY]),
        }
        // The above implicit returns.
    }
}
