from collections import namedtuple
from typing import Optional
import time

# us: int, them: int, share: int
Board = namedtuple('Board', ['us', 'them', 'share'])
# The bitboard-like representation here attempts to be as consistent
# with other implementations as much as possible, such that
# `us`, `them` and `share` should be 64-bit integers,
# though native Python int does not enable a fine control over this.
# Native Python int is chosen as the data type regardless,
# to enable quicker access to the values.


# The following are defined as global constants to make the code
# more readable, while some values have been inlined for use in match statements.

# Weights for representing win, loss and draw outcomes.
INFINITY     = 1000000
OUTCOME_WIN  = INFINITY
OUTCOME_DRAW = 0
OUTCOME_LOSS = -INFINITY

# Weights for line scoring.
BIG_TWO_COUNT   = 90
BIG_ONE_COUNT   = 20
SMALL_TWO_COUNT = 8
SMALL_ONE_COUNT = 1

# Weights for positional scoring.
CENTRE = 9
CORNER = 7
EDGE   = 5
SQ_BIG = 25

# Masks for use in changing bitboards.
LINE     = 0b111
CHUNK    = 0b111_111_111
DBLCHUNK = (CHUNK << 9) | CHUNK
EXCLZONE = (0b111111 << 58) | ((1 << 54) - 1)
CORNER_MASK = 0b101_000_101
EDGE_MASK   = 0b010_101_010
CENTRE_MASK = 0b000_010_000

# Precomputed values for the heuristic function
# are not stored as global values, to enable faster access.

# This function is to be executed at the very start, and only once,
# to populate the lists to be used in the heuristic evaluation.
def init() -> (list[int], list[int]):
    # These lookup tables store evaluations for different arrangements of grids,
    # for both small and large grid metrics.
    # These tables will essentially store partial heuristic evaluations
    # for all possible small grid arrangements in the game.
    eval_table_large = [0] * 262144
    eval_table_small = [0] * 262144
    pop_count = [0] * 512
    for i in range(512):
        pop_count[i] = sum((i >> j) & 1 for j in range(9))
    for us in range(512):
        for them in range(512):
            eval_large = eval_small = 0
            us_lines, them_lines = lines(us), lines(them)
            us_won = them_won = False
            for i in range(0, 24, 3):
                us_count = pop_count[(us_lines >> i) & LINE]
                them_count = pop_count[(them_lines >> i) & LINE]
                if us_count != 0 and them_count != 0:
                    continue
                if us_count == 3:
                    us_won = True
                    break
                if them_count == 3:
                    them_won = True
                    break
                eval_large += (BIG_TWO_COUNT if us_count == 2
                               else BIG_ONE_COUNT if us_count == 1
                               else 0)
                eval_large -= (BIG_TWO_COUNT if them_count == 2
                               else BIG_ONE_COUNT if them_count == 1
                               else 0)
                eval_small += (SMALL_TWO_COUNT if us_count == 2
                               else SMALL_ONE_COUNT if us_count == 1
                               else 0)
                eval_small -= (SMALL_TWO_COUNT if them_count == 2
                               else SMALL_ONE_COUNT if them_count == 1
                               else 0)
            eval_pos = (CORNER * (pop_count[us & CORNER_MASK] - pop_count[them & CORNER_MASK]) +
                        EDGE   * (pop_count[us &   EDGE_MASK] - pop_count[them &   EDGE_MASK]) +
                        CENTRE * (pop_count[us & CENTRE_MASK] - pop_count[them & CENTRE_MASK]))
            if us_won:
                eval_table_large[(them << 9) | us] = OUTCOME_WIN
            elif them_won:
                eval_table_large[(them << 9) | us] = OUTCOME_LOSS
            elif pop_count[us | them] == 9:
                eval_table_large[(them << 9) | us] = OUTCOME_DRAW
            else:
                eval_table_large[(them << 9) | us] = eval_large + eval_pos * SQ_BIG
                eval_table_small[(them << 9) | us] = eval_small + eval_pos
    return eval_table_large, eval_table_small

# Stores the occupancies of each of the eight lines in a 3x3 grid
# in the form of 24 bits, interpreted as eight sets of 3 bits.
# Every set of three bits in the returned value represents the occupancies
# of each of the eight lines in a 3x3 grid.
# Using the values internally representing each of the 9 zones, this function returns
# an unsigned integer with the least significant 24 bits in the format:
# 246 048 678 345 012 258 147 036.
def lines(grid: int) -> int:
    return (
        ((grid >> 0) & 1) * 0b_000_100_000_000_100_000_000_100 +
        ((grid >> 1) & 1) * 0b_000_000_000_000_010_000_100_000 +
        ((grid >> 2) & 1) * 0b_100_000_000_000_001_100_000_000 +
        ((grid >> 3) & 1) * 0b_000_000_000_100_000_000_000_010 +
        ((grid >> 4) & 1) * 0b_010_010_000_010_000_000_010_000 +
        ((grid >> 5) & 1) * 0b_000_000_000_001_000_010_000_000 +
        ((grid >> 6) & 1) * 0b_001_000_100_000_000_000_000_001 +
        ((grid >> 7) & 1) * 0b_000_000_010_000_000_000_001_000 +
        ((grid >> 8) & 1) * 0b_000_001_001_000_000_001_000_000
    )

# The functions below all assume that we are starting with a valid board position.
# Only valid positions will be reached if the program only ever uses its own functions
# to play moves on the boards.

def generate_moves(board: Board) -> list[int]:
    us, them, share = board
    data1 = lines((share >> 36) & CHUNK) # Contains all big grid lines for X
    data2 = lines((share >> 45) & CHUNK) # Contains all big grid lines for O

    # If either X or O has made a big grid three-in-a-row, the game is over.
    for i in range(0, 24, 3):
        if ((data1 >> i) & LINE) == LINE or ((data2 >> i) & LINE) == LINE:
            return []

    # List for all legal moves, to be returned.
    move_list = []

    # Here, we reuse `data1` and `data2` variables.
    data1 = us | them                               # Contains all occupancies in zones NW to SW
    data2 = ((share >> 18) | share) & DBLCHUNK      # Contains all occupancies in zones S to SE
    large = ((share >> 36) | (share >> 45)) & CHUNK # Contains all occupancies in big grid

    # Extract the zone to be played from the board.
    zone = (share >> 54) & 0b1111

    match zone:
        # The number `9` is used to represent the ability to move to any zone.
        case 9:
            # If the player is allowed to play in any zone they wish, select all blank squares
            # that are not in a zone that has a corresponding occupied large grid.

            # Since some of the zones are stored in separate fields of Board,
            # two separate iterations are needed to cover all squares.
            move_list.extend(
                i for i in range(63)
                if not (((data1 >> i) & 1) or ((large >> (i//9)) & 1)))
            move_list.extend(
                i for i in range(63, 81)
                if not (((data2 >> (i-63)) & 1) or ((large >> (i//9)) & 1)))

        # In the following, we will assume that the *zone* value given corresponds to a zone that
        # does not correspond to an occupied space in the large grid.

        # Zones S and SE are stored in `share`, rather than `us` and `them`.
        case 7 | 8:
            # We only use the value of 9*zone from here on, hence we update `zone` itself.
            zone *= 9
            # Bitshift so that the nine least significant bits of `data2`
            # represent the relevant zone.
            data2 >>= zone - 63
            move_list.extend(zone + i for i in range(9) if not ((data2 >> i) & 1))

        case _:
            # We only use the value of 9*zone from here on, hence we update `zone` itself.
            zone *= 9
            # Bitshift so that the nine least significant bits of `data1`
            # represent the relevant zone.
            data1 >>= zone
            move_list.extend(zone + i for i in range(9) if not ((data1 >> i) & 1))

    return move_list

# For a given move played by a given player, returns the new board state.
# Does not mutate the board object passed to this function.
def play_move(board: Board, move: int, side: bool) -> Board:
    us, them, share = board

    # `line_occupy` contains our line occupancies of the zone we place in this move.
    if move > 62:
        # If the zone is S or SE, `share` is updated.
        # O updates 18 bits to the left from where X updates.
        # The switch between values of `18` and `0` are done using Python's "truthy" `and` operator.
        share |= 1 << (move - 63 + (side and 18))
        line_occupy = lines((share >> (9*(move // 9) - 63 + (side and 18))) & CHUNK)
    elif not side:
        # If Player X is playing, `us` is updated.
        us |= 1 << move
        line_occupy = lines((us >> (9*(move // 9))) & CHUNK)
    else:
        # If Player O is playing, `them` is updated.
        them |= 1 << move
        line_occupy = lines((them >> (9*(move // 9))) & CHUNK)

    # `next_chunk` contains the occupancies of the next zone to be played in.
    if move % 9 > 6: # If the next zone is S or SE, we access `share`.
        next_chunk = ((share | (share >> 18)) >> (9*((move % 9) - 7))) & CHUNK
    else: # Otherwise, we access `us` and `them`.
        next_chunk = ((us | them) >> (9*(move % 9))) & CHUNK

    # Check whether any of the lines in the current zone have been won.
    line_won = any(((line_occupy >> i) & LINE) == LINE for i in range(0, 24, 3))
    if line_won:
        # Update the large grid if this move wins a zone.
        share |= 1 << (36 + 9 * side + move // 9)

    # The next zone is determined by `move % 9` if the target zone is not won or fully occupied,
    # but the next move can be in any zone (represented as `9` internally) otherwise.
    if next_chunk == CHUNK or (((share | (share >> 9)) >> (36 + move % 9)) & 1):
        zone = 9
    else:
        zone = move % 9

    return Board(us, them, (share & EXCLZONE) | (zone << 54))

# Heuristic for evaluating a particular board state for a given side.
# This function uses the precomputed values stored in two lists, calculated upon program startup.
def evaluate(board: Board, side: bool, *, tables: (list[int], list[int])) -> int:

    us, them, share = board

    # We access the precomputed tables, passed as parameters to enable faster access.
    eval_table_large, eval_table_small = tables

    # First, we check the evaluation of the large grid.
    eval = eval_table_large[(share >> 36) & DBLCHUNK]

    # If the large grid has a definitive win or loss outcome, the game is over,
    # and we toggle the win or loss to match with the side we are evaluating for.
    if eval == OUTCOME_WIN or eval == OUTCOME_LOSS:
        # `(1 - (side << 1))` toggles the evaluation by multiplying
        # by a factor of 1 if playing X, and -1 if playing O
        return eval * (1 - (side << 1))

    large = ((share >> 36) | (share >> 45)) & CHUNK
    if large == CHUNK:
        return OUTCOME_DRAW

    # Now, we loop through each of the 9 zones.
    for i in range(9):
        # We check for whether the zone is from NW to SW or S to SE with this if-statement.
        if i < 7:
            us_data = (us >> (9*i)) & CHUNK
            them_data = (them >> (9*i)) & CHUNK
        else:
            us_data = (share >> (9*i - 63)) & CHUNK
            them_data = (share >> (9*i - 45)) & CHUNK
        # If the zone is completely filled or the corresponding spot in the large grid is filled,
        # we do not score this zone.
        if (us_data | them_data) == CHUNK or ((large >> i) & 1):
            continue
        # Add the component of the evaluation to the running total.
        eval += eval_table_small[(them_data << 9) | us_data]

    return eval * (1 - (side << 1))

# The main alpha-beta minimax function. Returns evaluation and principal variation.
def alpha_beta(
        board: Board,
        side: bool,
        depth: int,
        alpha: int,
        beta: int,
        *,
        tables: (list[int], list[int]),
        max_depth: int) -> (int, list[Optional[int]]):

    eval_table_large, eval_table_small = tables
    # Retrieve the list of legal moves in this position.
    move_list = generate_moves(board)

    # If there are no legal moves, the game is over.
    if not move_list:
        eval = eval_table_large[(board.share >> 36) & DBLCHUNK] * (1 - (side << 1))
        if eval == OUTCOME_WIN:
            eval -= max_depth - depth
        elif eval == OUTCOME_LOSS:
            eval += max_depth - depth
        else:
            eval = OUTCOME_DRAW
        return eval, [None] * (max_depth - depth)

    # Reached leaf node, so return static evaluation and empty PV.
    if depth == 0:
        return evaluate(board, side, tables=tables), [None] * max_depth

    pv = [None] * max_depth

    # Iterate over all legal moves.
    for mv in move_list:
        # Recursive minimax call, using negamax construct as evaluation is symmetric.
        eval, line = alpha_beta(
            play_move(board, mv, side),
            not side,
            depth - 1,
            -beta,
            -alpha,
            tables=tables,
            max_depth=max_depth)

        # So, we take the negative of the evaluation.
        eval = -eval
        line[max_depth - depth] = mv # Record this move in the line.

        if eval >= beta: # Fail hard beta-cutoff
            return beta, line
        elif eval > alpha: # New best move found
            alpha = eval
            pv = line # Update PV.

    return alpha, pv

def print_board(board: Board):
    sq_arr = ['NW', 'N', 'NE', 'W', 'C', 'E', 'SW', 'S', 'SE']
    US, THEM, BLANK = 'X', 'O', '.'
    us, them, share = board
    small = [''] * 81
    large = [''] * 9
    zone = (share >> 54) & 0b1111
    for i in range(63):
        small[i] = US if (us >> i) & 1 else THEM if (them >> i) & 1 else BLANK
    for i in range(18):
        small[i + 63] = US if (share >> i) & 1 else THEM if (share >> (i + 18)) & 1 else BLANK
    for i in range(9):
        large[i] = US if (share >> (i + 36)) & 1 else THEM if (share >> (i + 45)) & 1 else BLANK
    print('---+---+---')
    for i in range(0, 81, 27):
        for j in range(0, 9, 3):
            print('|'.join(''.join(small[i+j+k:i+j+k+3]) for k in range(0, 27, 9)))
        print('---+---+---')
    for i in range(0, 9, 3):
        print(''.join(large[i:i+3]))
    print('ZONE: ' + ('ANY' if zone == 9 else sq_arr[zone]))

def move_string(move: int) -> str:
    sq_arr = ['nw', 'n', 'ne', 'w', 'c', 'e', 'sw', 's', 'se']
    return sq_arr[move // 9] + '/' + sq_arr[move % 9]

def eval_string(eval: int, *, max_depth: int) -> str:
    if eval <= OUTCOME_LOSS + max_depth:
        return 'L' + str(eval - OUTCOME_LOSS)
    elif eval >= OUTCOME_WIN - max_depth:
        return 'W' + str(OUTCOME_WIN - eval)
    elif eval == OUTCOME_DRAW:
        return 'D0'
    else:
        return '{:+0}'.format(eval)

def input_player_move(possible_moves: list[int]) -> int:
    sq_arr = ['nw', 'n', 'ne', 'w', 'c', 'e', 'sw', 's', 'se']
    while True:
        inputted_move = input('Move: ')
        if inputted_move.count('/') != 1:
            continue
        zone, square = inputted_move.split('/')
        if zone not in sq_arr or square not in sq_arr:
            continue
        move = 9 * sq_arr.index(zone) + sq_arr.index(square)
        if move in possible_moves:
            return move

def main():
    while True:
        try:
            depth = int(input('Depth: '))
            break
        except ValueError:
            continue
    c = input()
    while c not in '12':
        c = input()
    self_start = c == '1'
    print('Playing X' if self_start else 'Playing O')

    board = Board(0, 0, 9 << 54)
    player = False

    eval_table_large, eval_table_small = init()

    if self_start:
        s = time.perf_counter()
        eval, line = alpha_beta(
            board,
            False,
            depth,
            -INFINITY,
            INFINITY,
            tables=(eval_table_large, eval_table_small),
            max_depth=depth)
        e = time.perf_counter()
        board = play_move(board, line[0], False)
        string_moves = [move_string(mv) for mv in line]
        print('AI Move: {0} PV: [{1}] Eval: {2} Time elapsed: {3} ms'.format(
            string_moves[0],
            ', '.join(string_moves),
            eval_string(eval, max_depth=depth),
            int(1000 * (e-s))
        ))
        player = True

    print_board(board)

    while True:
        possible_moves = generate_moves(board)
        if not possible_moves:
            print('Game over')
            input()
            break
        move = input_player_move(possible_moves)
        board = play_move(board, move, player)
        print_board(board)
        if not generate_moves(board):
            print('Game over')
            input()
            break
        s = time.perf_counter()
        eval, line = alpha_beta(
            board,
            not player,
            depth,
            INFINITY,
            -INFINITY,
            tables=(eval_table_large, eval_table_small),
            max_depth=depth
        )
        e = time.perf_counter()
        board = play_move(board, line[0], not player)
        string_moves = [move_string(mv) for mv in line]
        print('AI Move: {0} PV: [{1}] Eval: {2} Time elapsed: {3} ms'.format(
            string_moves[0],
            ', '.join(string_moves),
            eval_string(eval, max_depth=depth),
            int(1000 * (e-s))
        ))
        print_board(board)

if __name__ == '__main__':
    main()