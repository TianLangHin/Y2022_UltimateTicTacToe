import sys
import time

INFINITY = 1000000

OUTCOME_WIN = INFINITY
OUTCOME_DRAW = 0
OUTCOME_LOSS = -INFINITY

SEARCHING_DEPTH = int(sys.argv[1])
US = 'X'
THEM = 'O'

LINES = tuple([slice(i, 9, 3) for i in range(3)] + [slice(3*i, 3*i+3) for i in range(3)] + [slice(0, 9, 4), slice(2, 8, 2)])

def move_gen(board):
	small_grids, large_grid, zone = board
	lines = [large_grid[line] for line in LINES]
	if any(line.count(US) == 3 or line.count(THEM) == 3 for line in lines):
		return []
	if zone == -1:
		move_list = [i for i in range(81) if small_grids[i // 9][i % 9] == '.' and large_grid[i // 9] == '.']
	else:
		move_list = [9*zone + i for i in range(9) if small_grids[zone][i] == '.']
	move_list.sort(key=(lambda m: small_grids[m // 9].count(US) - small_grids[m // 9].count(THEM)), reverse=True)
	return move_list

def play_move(board, move, side):
	small_grids, large_grid, zone = board
	square_focus = small_grids[move // 9]
	square_focus = square_focus[:move % 9] + (US if side else THEM) + square_focus[(move % 9) + 1:]
	lines = [square_focus[line] for line in LINES]
	if side and any(line.count(US) == 3 for line in lines):
		large_grid = large_grid[:move // 9] + US + large_grid[(move // 9) + 1:]
	elif (not side) and any(line.count(THEM) == 3 for line in lines):
		large_grid = large_grid[:move // 9] + THEM + large_grid[(move // 9) + 1:]
	if '.' not in square_focus or large_grid[move % 9] != '.':
		zone = -1
	else:
		zone = move % 9
	small_grids = (*small_grids[:move // 9], square_focus, *small_grids[(move // 9)+1:])
	return (small_grids, large_grid, zone)

def evaluate(board, side):
	small_grids, large_grid, zone = board
	us, them = (US, THEM) if side else (THEM, US)
	big_lines = [large_grid[line] for line in LINES]
	eval = 0
	for line in big_lines:
		match line.count(us):
			case 3:
				return OUTCOME_WIN
			case 2:
				eval += 200
			case 1:
				eval += 100
		match line.count(them):
			case 3:
				return OUTCOME_LOSS
			case 2:
				eval -= 200
			case 1:
				eval -= 100
	if '.' not in large_grid:
		return OUTCOME_DRAW
	i = 0
	for i in range(9):
		if '.' not in small_grids[i] or large_grid[i] != '.':
			i += 1
			continue
		lines = [small_grids[i][line] for line in LINES]
		for line in lines:
			match line.count(us):
				case 2:
					eval += 25
				case 1:
					eval += 10
			match line.count(them):
				case 2:
					eval -= 25
				case 1:
					eval -= 10
	return eval

def print_board(board):
	square_array = ['nw', 'n', 'ne', 'w', 'c', 'e', 'sw', 's', 'se']
	small_grids, large_grid, zone = board
	print('---+---+---')
	for i in range(0, 81, 27):
		big_row = small_grids[i // 9 : (i // 9) + 3]
		for j in range(0, 9, 3):
			print('|'.join([x[j:j+3] for x in big_row]))
		print('---+---+---')
	for i in range(3):
		print(large_grid[3*i:3*i+3])
	print('ZONE:', 'ANY' if zone == -1 else square_array[zone].upper())

def alpha_beta(board, side, depth, alpha, beta, prev_line):
	move_list = move_gen(board)
	if len(move_list) == 0:
		us, them = (US, THEM) if side else (THEM, US)
		return (OUTCOME_WIN + depth - SEARCHING_DEPTH if any(board[1][line].count(us) == 3 for line in LINES) else \
			OUTCOME_LOSS - depth + SEARCHING_DEPTH if any(board[1][line].count(them) == 3 for line in LINES) else OUTCOME_DRAW), prev_line
	if depth == 0:
		return evaluate(board, side), prev_line
	pv = prev_line
	for move in move_list:
		eval, line = alpha_beta(play_move(board, move, side),
			not side, depth-1, -beta, -alpha, prev_line + [move])
		eval = -eval
		if eval >= beta:
			return beta, line
		elif eval > alpha or len(pv) == 0:
			alpha = eval
			pv = line
	return alpha, pv

def move_string(move):
	square_array = ['nw', 'n', 'ne', 'w', 'c', 'e', 'sw', 's', 'se']
	return square_array[move // 9] + '/' + square_array[move % 9]

def main():
	square_array = ['nw', 'n', 'ne', 'w', 'c', 'e', 'sw', 's', 'se']
	board = (
		('.........', '.........', '.........',
		'.........', '.........', '.........',
		'.........', '.........', '.........',), '.........', -1)
	player = True
	if len(sys.argv) == 2:
		t_start = time.perf_counter()
		eval, line = alpha_beta(board, True, SEARCHING_DEPTH, -INFINITY, INFINITY, [])
		t_end = time.perf_counter()
		board = play_move(board, line[0], True)
		line = [move_string(mv) for mv in line]
		print('AI Move:', line[0], 'PV:', '['+', '.join(line)+']', 'Eval:', eval, 'Time elapsed:', t_end - t_start, 's')
		player = False
	print_board(board)
	while True:
		possible_moves = move_gen(board)
		if len(possible_moves) == 0:
			print('Game over')
			break
		move = input('Move: ')
		zone, square = move.lower().split('/')
		move = 9 * square_array.index(zone) + square_array.index(square)
		while move not in possible_moves:
			move = input('Move: ')
			zone, square = move.lower().split('/')
			move = 9 * square_array.index(zone) + square_array.index(square)
		board = play_move(board, move, player)
		print_board(board)
		t_start = time.perf_counter()
		eval, line = alpha_beta(board, not player, SEARCHING_DEPTH, -INFINITY, INFINITY, [])
		t_end = time.perf_counter()
		if len(move_gen(board)) == 0:
			print('Game over')
			break
		board = play_move(board, line[0], not player)
		line = [move_string(mv) for mv in line]
		print('AI Move:', line[0], 'PV:', '['+', '.join(line)+']', 'Eval:', eval, 'Time elapsed:', t_end - t_start, 's')
		print_board(board)

if __name__ == '__main__':
	main()