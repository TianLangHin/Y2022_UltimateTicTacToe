use std::io::{stdin, stdout, Write};
use std::time::Instant;

/**
 * The bitboard structure is represented here as a tuple of 3 `u64`s.
 * Though the elements do not have inherent names, all elements
 * are always destructured with the `us`, `them` and `share` names.
 */
type Board = (u64, u64, u64);

// Weights for representing win, loss and draw outcomes.
const OUTCOME_WIN: i32 = 1000000;
const OUTCOME_DRAW: i32 = 0;
const OUTCOME_LOSS: i32 = -1000000;

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

// Internal representation for `zone` value, outside of the 0-8 range,
// to indicate that the player can play in any zone.
const ZONE_ANY: u64 = 9;

// Masks for use in changing bitboards.
const LINE: u64 = 0b111;
const CHUNK: u64 = 0b111111111;
const DBLCHUNK: u64 = (CHUNK << 9) | CHUNK;
const EXCLZONE: u64 = !(0b1111u64 << 54);
const CORNER_MASK: u64 = 0b_101_000_101;
const EDGE_MASK: u64 = 0b_010_101_010;
const CENTRE_MASK: u64 = 0b_000_010_000;

// Arrays to readily convert integers in the 0-8 range to the
// name of their corresponding zone.
const ZONE_ARRAY_UPPER: [&str; 9] = ["NW", "N", "NE", "W", "C", "E", "SW", "S", "SE"];
const ZONE_ARRAY_LOWER: [&str; 9] = ["nw", "n", "ne", "w", "c", "e", "sw", "s", "se"];

// Since values 0-80 are best used for move representation,
// 81 is used to represent a "null move".
const NULL_MOVE: u64 = 81;

// The absolute upper bound of total plies for this game is 81,
// since there are exactly 81 places that can be played.
const MAX_PLY: usize = 81;

/**
 * Due to the potential unreadability of an if-block in an arithmetic expression,
 * the `toggle_shift` and `toggle_eval` functions provide functions to adjust
 * a number based on a `bool` flag.
 */
#[inline(always)]
const fn toggle_shift(side: bool, num: u64) -> u64 {
    // Returns either the given `u64` or `0`.
    if side {
        num
    } else {
        0
    }
}

#[inline(always)]
const fn toggle_eval(side: bool, num: i32) -> i32 {
    // Returns either the given `i32` or the negative of it.
    if side {
        -num
    } else {
        num
    }
}

/**
 * Stores the occupancies of each of the eight lines in a 3x3 grid
 * in the form of 24 bits, interpreted as eight sets of 3 bits.
 * Every set of three bits in the returned value represents the occupancies
 * of each of the eight lines in a 3x3 grid.
 * Using the values internally representing each of the 9 zones, this function returns
 * an unsigned integer with the least significant 24 bits in the format:
 * 246 048 678 345 012 258 147 036.
 */
#[inline(always)]
const fn lines(grid: u64) -> u64 {
    (grid & 1) * 0b_000_100_000_000_100_000_000_100
        + ((grid >> 1) & 1) * 0b_000_000_000_000_010_000_100_000
        + ((grid >> 2) & 1) * 0b_100_000_000_000_001_100_000_000
        + ((grid >> 3) & 1) * 0b_000_000_000_100_000_000_000_010
        + ((grid >> 4) & 1) * 0b_010_010_000_010_000_000_010_000
        + ((grid >> 5) & 1) * 0b_000_000_000_001_000_010_000_000
        + ((grid >> 6) & 1) * 0b_001_000_100_000_000_000_000_001
        + ((grid >> 7) & 1) * 0b_000_000_010_000_000_000_001_000
        + ((grid >> 8) & 1) * 0b_000_001_001_000_000_001_000_000
}

/**
 * This function is to be executed at the very start, and only once,
 * to populate the lookup tables to be used in the heuristic evaluation.
 * Since each of the two lookup tables contain 262144 `i32` values,
 * Vec<i32> is returned instead of an array, to prevent stack overflow.
 */
fn init() -> (Vec<i32>, Vec<i32>) {
    // These lookup tables store evaluations for different arrangements of grids,
    // for both small and large grid metrics.
    // These tables will essentially store partial heuristic evaluations
    // for all possible small grid arrangements in the game.

    // These values are calculated from the perspective of player X,
    // so will have to be negated for player O,
    // as this program uses a symmetrical heuristic.

    let mut eval_table_large: Vec<i32> = vec![0; 262144];
    let mut eval_table_small: Vec<i32> = vec![0; 262144];
    let pop_count: Vec<i32> = (0..512)
        .map(|i| (0..9).fold(0, |acc, j| acc + ((i >> j) & 1)))
        .collect();
    for us in (0..512).map(|us| us as u64) {
        for them in (0..512).map(|them| them as u64) {
            let mut eval_large: i32 = 0;
            let mut eval_small: i32 = 0;
            let us_lines = lines(us);
            let them_lines = lines(them);
            let mut us_won: bool = false;
            let mut them_won: bool = false;
            for i in (0..24).step_by(3) {
                let us_count = pop_count[((us_lines >> i) & LINE) as usize];
                let them_count = pop_count[((them_lines >> i) & LINE) as usize];
                if us_count != 0 && them_count != 0 {
                    continue;
                }
                if us_count == 3 {
                    us_won = true;
                    break;
                }
                if them_count == 3 {
                    them_won = true;
                    break;
                }
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
            let eval_pos = CORNER
                * (pop_count[(us & CORNER_MASK) as usize]
                    - pop_count[(them & CORNER_MASK) as usize])
                + EDGE
                    * (pop_count[(us & EDGE_MASK) as usize]
                        - pop_count[(them & EDGE_MASK) as usize])
                + CENTRE
                    * (pop_count[(us & CENTRE_MASK) as usize]
                        - pop_count[(them & CENTRE_MASK) as usize]);

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

    (eval_table_large, eval_table_small)
}

/**
 * The functions below all assume that we are starting with a valid board position.
 * Only valid positions will be reached if the program only ever uses its own functions
 * to play moves on the boards.
 */

// To avoid running `.collect()` once every time a move list is generated
// only to be iterated over again in the functions it is used in,
// this function instead returns an boxed iterator trait object.
fn generate_moves(board: Board) -> Box<dyn Iterator<Item = u64>> {

    let (us, them, share) = board;

    let data1 = lines((share >> 36) & CHUNK); // Contains all big grid lines for X
    let data2 = lines((share >> 45) & CHUNK); // Contains all big grid lines for O

    // If either X or O has made a big grid three-in-a-row, the game is over.
    if (0..24)
        .step_by(3)
        .any(|i| ((data1 >> i) & LINE) == LINE || ((data2 >> i) & LINE) == LINE)
    {
        // No legal moves can be made when the game is over,
        // so we return a boxed empty iterator.
        return Box::new(0..0);
    }

    // Extract the zone to be played from the board.
    let zone = (share >> 54) & 0b1111;

    // We will reuse the `data1` and `data2` variables as needed
    // in this implicit returning match block.

    // Additionally, since the moves themselves correspond to actual integers that
    // can be used to directly access the bitboards, we only use filters and chains
    // without needing to map the value being tested to another value.

    match zone {
        // If the player is allowed to play in any zone they wish, select all blank squares
        // that are not in a zone that has a corresponding occupied large grid.
        ZONE_ANY => {

            // `data1` stores the combined occupancies in the NW to SW zones.
            // `data2` stores the combined occupancies in the S to SE zones.
            // `large` stores the combined occupancies of the large grid.
            let data1 = us | them;
            let data2 = ((share >> 18) | share) & DBLCHUNK;
            let large = ((share >> 36) | (share >> 45)) & CHUNK;

            // To prevent evaluating an extra condition at each iteration to determine
            // which identifier to access, two iterators are chained together.
            // The first iterator goes through all cells in the NW to SW zones.
            // The second iterator goes through all cells in the S to SE zones.
            Box::new(
                (0..63)
                    .filter(move |i| ((data1 >> i) & 1) == 0 && ((large >> (i / 9)) & 1) == 0)
                    .chain((63..81).filter(move |i| {
                        ((data2 >> (i - 63)) & 1) == 0 && ((large >> (i / 9)) & 1) == 0
                    })),
            )
        }
        // For zones S and SE, we access `share`, reusing `data2`.
        7 | 8 => {
            // We only use the value of `9 * zone` from here on,
            // hence we only multiply once and shadow the previous value.
            let zone = 9 * zone;
            let data2 = ((share >> 18) | share) & DBLCHUNK;
            Box::new((zone..zone + 9).filter(move |i| ((data2 >> (i - 63)) & 1) == 0))
        }
        // For zones NW to SW, we access `us` and `them`, reusing `data1`.
        _ => {
            // We only use the value of `9 * zone` from here on,
            // hence we only multiply once and shadow the previous value.
            let zone = 9 * zone;
            let data1 = us | them;
            Box::new((zone..zone + 9).filter(move |i| ((data1 >> i) & 1) == 0))
        }
    }
}

/**
 * For a given move played by a given player, returs the new board state.
 * Since Board is a tuple of primitive types, copies should be cheap enough,
 * eliminating the desire to construct a function that mutates the passed Board.
 */
fn play_move(board: Board, mv: u64, side: bool) -> Board {

    // Each move makes an incremental change to the board,
    // so we create mutable copies of the `u64` components of the board.
    let (mut us, mut them, mut share) = board;

    // `line_occupy` contains the lines occupied by the given side as a bit array
    // after the correct part of the board has been updated by the given move and player.

    // The number `62` is the highest value the internal representation of a move can take
    // such that the move is made inside the NW to SW zones.

    let line_occupy = if mv > 62 {
        // Internally, `false` represents player X and `true` represents player O.
        // The data for player O is 18 bits to the left of X,
        // hence `toggle_shift` is used to toggle between the values of `0` and `18`.
        share |= 1 << (mv - 63 + toggle_shift(side, 18));

        // implicit return in block
        lines((share >> (9 * (mv / 9) - 63 + toggle_shift(side, 18))) & CHUNK)
    } else if !side {
        us |= 1 << mv;

        // implicit return in block
        lines((us >> (9 * (mv / 9))) & CHUNK)
    } else {
        them |= 1 << mv;

        // implicit return in block
        lines((them >> (9 * (mv / 9))) & CHUNK)
    };

    // `next_chunk` contains all occupancies of the zone we are moving next in.
    // The next zone to be played in is determined by the position of the current move
    // relative to other cells in its zone, found by `mv % 9`.

    // The number `6` corresponds to zone SW, which is the highest value the
    // internal representation of a zone can take such that it is within the NW to SW zones.

    let next_chunk = if mv % 9 > 6 {
        // Access `share` if the next zone is S or SE.
        ((share | (share >> 18)) >> (9 * ((mv % 9) - 7))) & CHUNK
    } else {
        // Access `us` and `them` otherwise.
        ((us | them) >> (9 * (mv % 9))) & CHUNK
    };

    // Since we can assume that the function is given a valid position and move,
    // whichever zone this move is playing in is assumed to not be occupied by
    // the opponent yet, hence `line_occupy` only checks for our occupancies
    // to see whether we make a line in this grid.
    if (0..24)
        .step_by(3)
        .any(|i| ((line_occupy >> i) & LINE) == LINE)
    {
        share |= 1 << (36 + toggle_shift(side, 9) + mv / 9);
    }

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
fn evaluate(board: Board, side: bool, tables: &(Vec<i32>, Vec<i32>)) -> i32 {

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

                // Zones that are comlpetely filled or correspond to an occupied large grid
                // are not scored. Since the values are added,
                // we return a zero for this situation.
                if ((large >> i) & 1) == 1 || (us_data | them_data) == CHUNK {
                    0
                } else {
                    // Incrementally add the precomputed evaluation of the small grid.
                    tables.1[((them_data << 9) | us_data) as usize]
                }
            }))
            .fold(eval, |acc, x| acc + x), // Add all these values on to the large grid evaluation.
    )
    // The above implicit returns.
}

/**
 * The main alpha-beta minimax function.
 * Uses a negamax construct since the heuristic is symmetric.
 * Returns evaluation and the principal variation.
 */
fn alpha_beta(
    board: Board,
    side: bool,
    depth: usize,
    mut alpha: i32, // The `alpha` variable will be updated throughout, and is cheaply copied.
    beta: i32,
    tables: &(Vec<i32>, Vec<i32>),
    max_depth: usize,
) -> (i32, [u64; MAX_PLY]) {

    // It is not always necessary to destructure the board,
    // as only one branch of this function uses one of the components as is.
    // The board is otherwise passed as is.

    // Once `depth` reaches zero, we stop the search here at the leaf node
    // and return the static evaluation, as well as an empty PV.
    if depth == 0 {
        return (evaluate(board, side, tables), [NULL_MOVE; MAX_PLY]);
    }

    // Since `generate_moves` returns an iterator
    // which we only want to iterate through once, we first do one call to `.next()`
    // to see if it is empty or not, before then consuming until `None`
    // if an element was returned at all.
    let mut move_list = generate_moves(board);

    // We retrieve an element once,
    // and making the element binding mutable for successive updates
    // when looping over all moves.
    if let Some(mut mv) = move_list.next() {

        // Initialise the container for the principal variation,
        // which will be updated as new better lines are found.
        let mut pv = [NULL_MOVE; MAX_PLY];

        // This is equivalent to a do-while loop,
        // since we have already accessed one element,
        // we execute the body of the loop once, before looking for next elements.
        loop {
            // For each move, do a recursive minimax call,
            // swapping and negating `alpha` and `beta`
            // as the heuristic is symmetric.
            let (mut eval, mut line) = alpha_beta(
                play_move(board, mv, side),
                !side,
                depth - 1,
                -beta,
                -alpha,
                tables,
                max_depth,
            );

            // Hence, we also take the negative of the evaluation.
            eval = -eval;

            // Record this move in the line.
            line[max_depth - depth] = mv;

            if eval >= beta {
                // Fail-hard beta cutoff:
                // returns the bound as this line will never be searched again,
                // since it is determined to be suboptimal.
                return (beta, line);
            } else if eval > alpha {
                // New best move found.
                alpha = eval;
                // Update PV.
                pv = line;
            }

            // Here, we retrieve the next move, and break out of the loop
            // when `None` is returned.
            if let Some(new_mv) = move_list.next() {
                // Update the `mv` binding with the new returned move.
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
            OUTCOME_WIN => (
                eval - (max_depth - depth) as i32,
                [NULL_MOVE; MAX_PLY],
            ),
            OUTCOME_LOSS => (
                eval + (max_depth - depth) as i32,
                [NULL_MOVE; MAX_PLY],
            ),
            // If the large grid does not give a win or loss
            // but there are no legal moves, the game is drawn.
            _ => (OUTCOME_DRAW, [NULL_MOVE; MAX_PLY]),
        }

        // The above implicit returns.
    }
}

fn print_board(board: Board) {
    let (us, them, share) = board;
    let zone = (share >> 54) & 0b1111;
    let small = (0..63)
        .map(|i| {
            if ((us >> i) & 1) == 1 {
                "X".to_string()
            } else if ((them >> i) & 1) == 1 {
                "O".to_string()
            } else {
                ".".to_string()
            }
        })
        .chain((0..18).map(|i| {
            if ((share >> i) & 1) == 1 {
                "X".to_string()
            } else if ((share >> (i + 18)) & 1) == 1 {
                "O".to_string()
            } else {
                ".".to_string()
            }
        }))
        .collect::<Vec<_>>();
    let large = (0..9)
        .map(|i| {
            if ((share >> (i + 36)) & 1) == 1 {
                "X".to_string()
            } else if ((share >> (i + 45)) & 1) == 1 {
                "O".to_string()
            } else {
                ".".to_string()
            }
        })
        .collect::<Vec<_>>();
    println!("---+---+---");
    for i in (0..81).step_by(27) {
        for j in (0..9).step_by(3) {
            println!(
                "{}",
                (0..27)
                    .step_by(9)
                    .map(|k| small[i + j + k..i + j + k + 3].join(""))
                    .collect::<Vec<_>>()
                    .join("|")
            );
        }
        println!("---+---+---");
    }
    for i in (0..9).step_by(3) {
        println!("{}", large[i..i + 3].join(""));
    }
    println!(
        "ZONE: {}",
        if zone == ZONE_ANY {
            "ANY"
        } else {
            ZONE_ARRAY_UPPER[zone as usize]
        }
    );
}

fn move_string(mv: u64) -> String {
    format!(
        "{0}/{1}",
        ZONE_ARRAY_LOWER[(mv / 9) as usize],
        ZONE_ARRAY_LOWER[(mv % 9) as usize]
    )
}

fn eval_string(eval: i32, max_depth: usize) -> String {
    if eval <= OUTCOME_LOSS + max_depth as i32 {
        format!("L{0}", eval - OUTCOME_LOSS)
    } else if eval >= OUTCOME_WIN - max_depth as i32 {
        format!("W{0}", OUTCOME_WIN - eval)
    } else if eval == OUTCOME_DRAW {
        "D0".to_string()
    } else {
        format!("{:+0}", eval)
    }
}

fn input_player_move(possible_moves: &[u64]) -> u64 {
    let mut input = String::new();
    loop {
        print!("Move: ");
        let _ = stdout().flush();
        if match stdin().read_line(&mut input) {
            Ok(_) => input.chars().filter(|&c| c == '/').count(),
            Err(_) => continue,
        } != 1
        {
            continue;
        }
        let mut components = input.trim().split('/');
        let zone = components.next().unwrap();
        let square = components.next().unwrap();
        if !ZONE_ARRAY_LOWER.contains(&zone) || !ZONE_ARRAY_LOWER.contains(&square) {
            println!("[{zone}][{square}]");
            continue;
        }
        match ZONE_ARRAY_LOWER.iter().position(|&z| z == zone) {
            Some(z) => match ZONE_ARRAY_LOWER.iter().position(|&s| s == square) {
                Some(s) => {
                    let mv = 9 * z as u64 + s as u64;
                    if possible_moves.contains(&mv) {
                        return mv;
                    }
                }
                None => continue,
            },
            None => continue,
        }
    }
}

fn main() {
    let mut input_depth: String;
    let depth: usize;
    loop {
        input_depth = String::new();
        print!("Depth: ");
        let _ = stdout().flush();
        match stdin().read_line(&mut input_depth) {
            Ok(_) => match input_depth.trim().parse::<usize>() {
                Ok(d) => {
                    depth = d;
                    break;
                }
                Err(_) => continue,
            },
            Err(_) => continue,
        }
    }
    let mut c: String;
    let self_start: bool;
    loop {
        c = String::new();
        match stdin().read_line(&mut c) {
            Ok(_) => match c.trim() {
                "1" => {
                    self_start = true;
                    break;
                }
                "2" => {
                    self_start = false;
                    break;
                }
                _ => continue,
            },
            Err(_) => continue,
        }
    }
    if self_start {
        println!("Playing X");
    } else {
        println!("Playing O");
    }

    let mut board: Board = (0, 0, 9 << 54);
    let player: bool;

    let tables = init();

    if self_start {
        let start = Instant::now();
        let (eval, line) = alpha_beta(
            board,
            false,
            depth,
            OUTCOME_LOSS,
            OUTCOME_WIN,
            &tables,
            depth,
        );
        let duration = start.elapsed().as_millis();
        board = play_move(board, line[0], false);
        println!(
            "AI Move: {0} PV: [{1}] Eval: {2} Time elapsed: {3} ms",
            move_string(line[0]),
            line.iter()
                .take_while(|&&m| m != NULL_MOVE)
                .map(|m| move_string(*m))
                .collect::<Vec<_>>()
                .join(", "),
            eval_string(eval, depth),
            duration
        );
        player = true;
    } else {
        player = false;
    }

    print_board(board);

    loop {
        let possible_moves: Vec<u64> = generate_moves(board).collect();
        if possible_moves.is_empty() {
            println!("Game over");
            let mut _stop = String::new();
            let _ = stdin().read_line(&mut _stop);
            break;
        }
        let mv = input_player_move(&possible_moves);
        board = play_move(board, mv, player);
        print_board(board);
        if Option::is_none(&generate_moves(board).next()) {
            println!("Game over");
            let mut _stop = String::new();
            let _ = stdin().read_line(&mut _stop);
            break;
        }
        let start = Instant::now();
        let (eval, line) = alpha_beta(
            board,
            !player,
            depth,
            OUTCOME_LOSS,
            OUTCOME_WIN,
            &tables,
            depth,
        );
        let duration = start.elapsed().as_millis();
        board = play_move(board, line[0], false);
        println!(
            "AI Move: {0} PV: [{1}] Eval: {2} Time elapsed: {3} ms",
            move_string(line[0]),
            line.iter()
                .take_while(|&&m| m != NULL_MOVE)
                .map(|m| move_string(*m))
                .collect::<Vec<_>>()
                .join(", "),
            eval_string(eval, depth),
            duration
        );
        print_board(board);
    }
}
