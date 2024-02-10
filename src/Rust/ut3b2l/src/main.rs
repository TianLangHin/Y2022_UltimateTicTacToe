use std::io::stdin;
use std::time::Instant;

use crate::engine::*;

pub mod engine;

// Used to output an ASCII art representation of the board.
fn print_board(board: Board) {
    // Destructure and retrieve values from board.
    let (us, them, share) = board;
    let zone = (share >> 54) & 0b1111;

    // Map each of the bits to the coresponding string representations.
    // That is, "X" for Player X, "O" for Player O, and "." for non-occupied.
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

    // Similar mapping for large grid.
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

    // After organising occupancies into Vec, iterate through and print.
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

// Converts a `u64` move representation to a string.
fn move_string(mv: Move) -> String {
    format!(
        "{0}/{1}",
        ZONE_ARRAY_LOWER[(mv / 9) as usize],
        ZONE_ARRAY_LOWER[(mv % 9) as usize]
    )
}

// Returns the internal move representation from its string representation.
fn move_from_string(move_string: &str) -> Option<Move> {
    let zone_and_square: Vec<_> = move_string.split('/').collect();
    if zone_and_square.len() != 2 {
        return None;
    }
    let zone = ZONE_ARRAY_LOWER
        .iter()
        .position(|&z| z == zone_and_square[0]);
    let square = ZONE_ARRAY_LOWER
        .iter()
        .position(|&s| s == zone_and_square[1]);
    if let (Some(z), Some(s)) = (zone, square) {
        Some(9 * z as u64 + s as u64)
    } else {
        None
    }
}

// Converts a `i32` heuristic evaluation value to a string.
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

// Compressed inline string representation for compact passing of Board setups.
fn board_string(board: Board) -> String {
    let (us, them, share) = board;
    let zone = (share >> 54) & 0b1111;
    let cells = (0..81).step_by(27).flat_map(move |i| {
        (0..9).step_by(3).map(move |j| {
            (0..27)
                .step_by(9)
                .flat_map(move |k| i + j + k..i + j + k + 3)
        })
    });
    format!(
        "{} {}",
        cells
            .map(|v| v
                .map(|i| {
                    if i > 62 {
                        if ((share >> (i - 63)) & 1) == 1 {
                            "x".to_string()
                        } else if ((share >> (i - 45)) & 1) == 1 {
                            "o".to_string()
                        } else {
                            ".".to_string()
                        }
                    } else if ((us >> i) & 1) == 1 {
                        "x".to_string()
                    } else if ((them >> i) & 1) == 1 {
                        "o".to_string()
                    } else {
                        ".".to_string()
                    }
                })
                .collect::<Vec<_>>()
                .join(""))
            .collect::<Vec<_>>()
            .join("/")
            .replace(".........", "9")
            .replace("........", "8")
            .replace(".......", "7")
            .replace("......", "6")
            .replace(".....", "5")
            .replace("....", "4")
            .replace("...", "3")
            .replace("..", "2")
            .replace('.', "1"),
        if zone == ZONE_ANY {
            "any"
        } else {
            ZONE_ARRAY_LOWER[zone as usize]
        }
    )
}

// Returns an internal board representation from its string representation.
fn board_from_string(board_string: &str) -> Option<Board> {
    let (mut us, mut them, mut share) = (0u64, 0u64, 0u64);
    let decompressed_string = board_string
        .replace('1', ".")
        .replace('2', "..")
        .replace('3', "...")
        .replace('4', "....")
        .replace('5', ".....")
        .replace('6', "......")
        .replace('7', ".......")
        .replace('8', "........")
        .replace('9', ".........");
    let cell_and_zone: Vec<_> = decompressed_string.split_whitespace().collect();
    if cell_and_zone.len() != 2 {
        return None;
    }
    let (cell, zone) = (cell_and_zone[0], cell_and_zone[1]);
    if let Some(z) = ZONE_ARRAY_LOWER.iter().position(|&z| z == zone) {
        share |= (z as u64) << 54;
    } else if zone == "any" {
        share |= ZONE_ANY << 54;
    } else {
        return None;
    }
    let rows: Vec<_> = cell.split('/').collect();
    if rows.len() != 9 {
        return None;
    }
    if rows.iter().any(|row| row.len() != 9) {
        return None;
    }
    decompressed_string
        .replace('/', "")
        .chars()
        .zip((0..81).step_by(27).flat_map(move |i| {
            (0..9).step_by(3).flat_map(move |j| {
                (0..27)
                    .step_by(9)
                    .flat_map(move |k| i + j + k..i + j + k + 3)
            })
        }))
        .for_each(|(c, i)| {
            if i > 62 {
                if c == 'x' {
                    share |= 1 << (i - 63);
                } else if c == 'o' {
                    share |= 1 << (i - 45);
                }
            } else if c == 'x' {
                us |= 1 << i;
            } else if c == 'o' {
                them |= 1 << i;
            }
        });
    let first_seven = us | them;
    let last_two = (share >> 18) | share;
    for i in 0..7 {
        if line_presence(first_seven >> (9 * i)) {
            share |= 1 << (36 + i);
        }
    }
    for i in 7..9 {
        if line_presence(last_two >> (9 * i - 63)) {
            share |= 1 << (36 + i);
        }
    }
    Some((us, them, share))
}

fn main() {

    let tables = init();
    println!("ready");

    let mut history: Vec<(Board, Move)> = Vec::new();

    history.push(((0, 0, ZONE_ANY << 54), NULL_MOVE));

    let mut command_string: String;
    let mut command: Vec<String>;

    loop {
        command_string = String::new();
        match stdin().read_line(&mut command_string) {
            Ok(_) => {
                command = command_string
                    .split_whitespace()
                    .map(|s| s.to_string())
                    .collect()
            }
            Err(_) => continue,
        }
        if command.is_empty() {
            continue;
        }
        match &command[0] as &str {
            "newgame" => {
                if command.len() < 3 {
                    println!("newgame invalid args");
                    continue;
                }
                let cells = &command[1];
                let zone = &command[2];
                if let Some(new_board) = board_from_string(&format!("{} {}", cells, zone)) {
                    history.clear();
                    history.push((new_board, NULL_MOVE));
                    println!("newgame ok");
                } else {
                    println!("newgame invalid pos");
                }
            }
            "go" => {
                if command.is_empty() {
                    println!("info error no depth");
                }
                let current_player = (history.len() & 1) == 0;
                if let Ok(depth) = command[1].parse::<usize>() {
                    if depth == 0 {
                        println!("info error invalid depth");
                        continue;
                    }
                    let board = history.last().unwrap().0;
                    let start = Instant::now();
                    let (eval, line) = alpha_beta(
                        board,
                        current_player,
                        depth,
                        OUTCOME_LOSS,
                        OUTCOME_WIN,
                        &tables,
                        depth,
                    );
                    let duration = start.elapsed().as_millis();
                    println!(
                        "info depth {} pv {} eval {} time {}",
                        depth,
                        line.iter()
                            .take_while(|&&m| m != NULL_MOVE)
                            .map(|m| move_string(*m))
                            .collect::<Vec<_>>()
                            .join(" "),
                        eval_string(eval, depth),
                        duration
                    );
                    history.push((play_move(board, line[0], current_player), line[0]));
                } else {
                    println!("info error invalid depth");
                }
            }
            "play" => {
                if command.len() != 2 {
                    println!("move invalid");
                    continue;
                }
                if command[1] == "null" {
                    let (last_board, last_move) = *history.last().clone().unwrap();
                    history.push((last_board, last_move));
                    println!("move pos {}", board_string(last_board));
                } else if let Some(mv) = move_from_string(&command[1]) {
                    let board = history.last().unwrap().0;
                    if Option::is_some(&generate_moves(board).find(|&m| m == mv)) {
                        let current_player = (history.len() & 1) == 0;
                        let new_board = play_move(board, mv, current_player);
                        history.push((new_board, mv));
                        println!("move pos {}", board_string(new_board));
                    } else {
                        println!("move illegal");
                    }
                } else {
                    println!("move invalid");
                }
            }
            "undo" => {
                if let Some((last_board, last_move)) = history.pop() {
                    if history.is_empty() {
                        history.push((last_board, last_move));
                        println!("undo stackempty");
                    } else {
                        println!("undo ok");
                    }
                } else {
                    println!("undo stackempty");
                }
            }
            "gamepos" => println!("{}", board_string(history.last().unwrap().0)),
            "d" => print_board(history.last().unwrap().0),
            "q" => break,
            _ => println!("badkeyword"),
        }
    }
}
