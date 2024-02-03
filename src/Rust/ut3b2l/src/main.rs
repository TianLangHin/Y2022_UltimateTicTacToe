use std::io::{stdin, stdout, Write};
use std::time::Instant;

type Board = (u64, u64, u64);

const OUTCOME_WIN: i32 = 1000000;
const OUTCOME_DRAW: i32 = 0;
const OUTCOME_LOSS: i32 = -1000000;

const BIG_TWO_COUNT: i32 = 90;
const BIG_ONE_COUNT: i32 = 20;
const SMALL_TWO_COUNT: i32 = 8;
const SMALL_ONE_COUNT: i32 = 1;

const CENTRE: i32 = 9;
const CORNER: i32 = 7;
const EDGE: i32 = 5;
const SQ_BIG: i32 = 25;

const ZONE_ANY: u64 = 9;

const LINE: u64 = 0b111;
const CHUNK: u64 = 0b111111111;
const DBLCHUNK: u64 = 0b_111111111_111111111;
const EXCLZONE: u64 = 0b_111111_0000_111111111_111111111_111111111_111111111_111111111_111111111;
const CORNER_MASK: u64 = 0b_101_000_101;
const EDGE_MASK: u64 = 0b_010_101_010;
const CENTRE_MASK: u64 = 0b_000_010_000;

const ZONE_ARRAY_UPPER: [&str; 9] = ["NW", "N", "NE", "W", "C", "E", "SW", "S", "SE"];
const ZONE_ARRAY_LOWER: [&str; 9] = ["nw", "n", "ne", "w", "c", "e", "sw", "s", "se"];

#[inline(always)]
const fn toggle_shift(side: bool, num: u64) -> u64 {
    if side {
        num
    } else {
        0
    }
}

#[inline(always)]
const fn toggle_eval(side: bool, num: i32) -> i32 {
    if side {
        -num
    } else {
        num
    }
}

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

fn init() -> (Vec<i32>, Vec<i32>) {
    let mut eval_table_large: Vec<i32> = vec![0; 262144];
    let mut eval_table_small: Vec<i32> = vec![0; 262144];
    let mut pop_count: [i32; 512] = [0; 512];
    for i in 0..512 {
        pop_count[i] = ((i & 1)
            + ((i >> 1) & 1)
            + ((i >> 2) & 1)
            + ((i >> 3) & 1)
            + ((i >> 4) & 1)
            + ((i >> 5) & 1)
            + ((i >> 6) & 1)
            + ((i >> 7) & 1)
            + ((i >> 8) & 1)) as i32;
    }
    for u in 0..512 {
        for t in 0..512 {
            let us = u as u64;
            let them = t as u64;
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
                };
                eval_large -= match them_count {
                    2 => BIG_TWO_COUNT,
                    1 => BIG_ONE_COUNT,
                    _ => 0,
                };
                eval_small += match us_count {
                    2 => SMALL_TWO_COUNT,
                    1 => SMALL_ONE_COUNT,
                    _ => 0,
                };
                eval_small -= match them_count {
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
    return (eval_table_large, eval_table_small);
}

fn generate_moves(board: Board) -> Box<dyn Iterator<Item = u64>> {
    let (us, them, share) = board;
    let data1 = lines((share >> 36) & CHUNK);
    let data2 = lines((share >> 45) & CHUNK);
    if (0..24)
        .step_by(3)
        .any(|i| ((data1 >> i) & LINE) == LINE || ((data2 >> i) & LINE) == LINE)
    {
        return Box::new(0..0);
    }
    let zone = (share >> 54) & 0b1111;
    match zone {
        ZONE_ANY => {
            let data1 = us | them;
            let data2 = ((share >> 18) | share) & DBLCHUNK;
            let large = ((share >> 36) | (share >> 45)) & CHUNK;
            Box::new((0..63)
                .filter(move |i| {
                    ((data1 >> i) & 1) == 0 && ((large >> (i / 9)) & 1) == 0
                })
                .chain((63..81).filter(move |i| {
                    ((data2 >> (i - 63)) & 1) == 0 && ((large >> (i / 9)) & 1) == 0
                })))
        }
        7 | 8 => {
            let z = 9 * zone;
            let data2 = ((share >> 18) | share) & DBLCHUNK;
            Box::new((z..z+9).filter(move |i| ((data2 >> (i - 63)) & 1) == 0))
        }
        _ => {
            let z = 9 * zone;
            let data1 = us | them;
            Box::new((z..z+9).filter(move |i| ((data1 >> i) & 1) == 0))
        }
    }
}

fn play_move(board: Board, mv: u64, side: bool) -> Board {
    let (mut us, mut them, mut share) = board;
    let line_occupy = if mv > 62 {
        share |= 1 << (mv - 63 + toggle_shift(side, 18));
        lines((share >> (9 * (mv / 9) - 63 + toggle_shift(side, 18))) & CHUNK)
    } else if !side {
        us |= 1 << mv;
        lines((us >> (9 * (mv / 9))) & CHUNK)
    } else {
        them |= 1 << mv;
        lines((them >> (9 * (mv / 9))) & CHUNK)
    };
    let next_chunk = if mv % 9 > 6 {
        ((share | (share >> 18)) >> (9 * ((mv % 9) - 7))) & CHUNK
    } else {
        ((us | them) >> (9 * (mv % 9))) & CHUNK
    };
    if (0..24)
        .step_by(3)
        .any(|i| ((line_occupy >> i) & LINE) == LINE)
    {
        share |= 1 << (36 + toggle_shift(side, 9) + mv / 9);
    }
    let zone = if next_chunk == CHUNK || (((share | (share >> 9)) >> (36 + mv % 9)) & 1) == 1 {
        ZONE_ANY
    } else {
        mv % 9
    };
    return (us, them, (share & EXCLZONE) | (zone << 54));
}

fn evaluate(board: Board, side: bool, tables: &(Vec<i32>, Vec<i32>)) -> i32 {
    let (us, them, share) = board;
    let eval = tables.0[((share >> 36) & DBLCHUNK) as usize];
    if eval == OUTCOME_WIN || eval == OUTCOME_LOSS {
        return toggle_eval(side, eval);
    }
    let large = ((share >> 36) | (share >> 45)) & CHUNK;
    if large == CHUNK {
        return OUTCOME_DRAW;
    }
    toggle_eval(
        side,
        (0..7)
            .map(|i| {
                let us_data = (us >> (9 * i)) & CHUNK;
                let them_data = (them >> (9 * i)) & CHUNK;
                if ((large >> i) & 1) == 1 || (us_data | them_data) == CHUNK {
                    0
                } else {
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
            .fold(eval, |acc, x| {
                acc + x
            }),
    )
}

fn alpha_beta(
    board: Board,
    side: bool,
    depth: usize,
    mut alpha: i32,
    beta: i32,
    tables: &(Vec<i32>, Vec<i32>),
    max_depth: usize,
) -> (i32, Vec<u64>) {

    let (_us, _them, share) = board;
    if depth == 0 {
        return (evaluate(board, side, tables), vec![0; max_depth]);
    }

    let mut move_list = generate_moves(board);
    if let Some(mut mv) = move_list.next() {
        let mut pv = vec![0; max_depth];
        loop {
            let (mut eval, mut line) = alpha_beta(
                play_move(board, mv, side),
                !side,
                depth - 1,
                -beta,
                -alpha,
                tables,
                max_depth,
            );
            eval = -eval;
            line[max_depth - depth] = mv;
            if eval >= beta {
                return (beta, line);
            } else if eval > alpha {
                alpha = eval;
                pv = line;
            }
            if let Some(new_mv) = move_list.next() {
                mv = new_mv;
            } else {
                break;
            }
        }
        return (alpha, pv);
    } else {
        let eval = toggle_eval(side, tables.0[((share >> 36) & DBLCHUNK) as usize]);
        return match eval {
            OUTCOME_WIN => (
                eval - (max_depth - depth) as i32,
                vec![0; max_depth - depth],
            ),
            OUTCOME_LOSS => (
                eval + (max_depth - depth) as i32,
                vec![0; max_depth - depth],
            ),
            _ => (OUTCOME_DRAW, vec![0; max_depth - depth]),
        };
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
    format!("{0}/{1}", ZONE_ARRAY_LOWER[(mv / 9) as usize], ZONE_ARRAY_LOWER[(mv % 9) as usize])
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

fn input_player_move(possible_moves: &Vec<u64>) -> u64 {
    let mut input = String::new();
    loop {
        print!("Move: ");
        let _ = stdout().flush();
        if match stdin().read_line(&mut input) {
            Ok(_) => input.chars().filter(|&c| c == '/').count(),
            Err(_) => continue
        } != 1 {
            continue;
        }
        let mut components = input.trim().split("/");
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
                None => continue
            }
            None => continue
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
                Ok(d) => { depth = d; break; },
                Err(_) => continue
            }
            Err(_) => continue
        }
    };
    let mut c: String;
    let self_start: bool;
    loop {
        c = String::new();
        match stdin().read_line(&mut c) {
            Ok(_) => match c.trim() {
                "1" => { self_start = true; break; }
                "2" => { self_start = false; break; }
                _ => continue
            }
            Err(_) => continue
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
            depth
        );
        let duration = start.elapsed().as_millis();
        board = play_move(board, line[0], false);
        println!(
            "AI Move: {0} PV: [{1}] Eval: {2} Time elapsed: {3} ms",
            move_string(line[0]),
            line.iter().map(|m| move_string(*m)).collect::<Vec<_>>().join(", "),
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
        if generate_moves(board).collect::<Vec<u64>>().is_empty() {
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
            OUTCOME_WIN,
            OUTCOME_LOSS,
            &tables,
            depth
        );
        let duration = start.elapsed().as_millis();
        board = play_move(board, line[0], false);
        println!(
            "AI Move: {0} PV: [{1}] Eval: {2} Time elapsed: {3} ms",
            move_string(line[0]),
            line.iter().map(|m| move_string(*m)).collect::<Vec<_>>().join(", "),
            eval_string(eval, depth),
            duration
        );
        print_board(board);
    }
}
