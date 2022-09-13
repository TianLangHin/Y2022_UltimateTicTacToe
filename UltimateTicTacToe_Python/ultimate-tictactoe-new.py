import time

LINES = tuple([slice(i, 9, 3) for i in range(3)] + [slice(3*i, 3*i+3) for i in range(3)] + [slice(0, 9, 4), slice(2, 8, 2)])

SQUARE_ARRAY = ('nw', 'n', 'ne', 'w', 'c', 'e', 'sw', 's', 'se')

SEARCHING_DEPTH = 0

def move_gen(board):
	small, large, zone = board
	large_lines = [large[line] for line in LINES]
	if any(x == [1, 1, 1] or x == [-1, -1, -1] for x in large_lines):
		return []
	return [
		i for i in range(81) if small[i // 9][i % 9] == 0 and large[i // 9] == 0
	] if zone == 9 else [
		9*zone+i for i in range(9) if small[zone][i] == 0
	]

def play_move(board, move, side):
	small, large, zone = board
	small, large = [[sq for sq in zone] for zone in small], [sq for sq in large]
	small[move // 9][move % 9] = 1 if side else -1
	lines = [small[move // 9][line] for line in LINES]
	if side and any(x == [1, 1, 1] for x in lines):
		large[move // 9] = 1
	elif (not side) and any(x == [-1, -1, -1] for x in lines):
		large[move // 9] = -1
	zone = 9 if 0 not in small[move % 9] or large[move % 9] != 0 else move % 9
	return small, large, zone

def evaluate(board, side):
	(small, large, zone) = board
	us, them = (1, -1) if side else (-1, 1)
	lines = [large[line] for line in LINES]
	eval = 0
	for i in range(8):
		line_us = lines[i].count(us)
		line_them = lines[i].count(them)
		if line_us > 0 and line_them > 0:
			continue
		match line_us:
			case 3:
				return 10000000
			case 2:
				eval += 90
			case 1:
				eval += 20
		match line_them:
			case 3:
				return -10000000
			case 2:
				eval -= 90
			case 1:
				eval -= 20
	if 0 not in large:
		return OUTCOME_DRAW
	for i in range(9):
		if 0 not in small[i] or large[i] != 0:
			continue
		lines = [small[i][line] for line in LINES]
		for j in range(8):
			line_us = lines[j].count(us)
			line_them = lines[j].count(them)
			if line_us > 0 and line_them > 0:
				continue
			match line_us:
				case 2:
					eval += 8
				case 1:
					eval += 1
			match line_them:
				case 2:
					eval -= 8
				case 1:
					eval -= 1
	sq_score = 7 * sum(small[i][0] + small[i][2] + small[i][6] + small[i][8] for i in range(9)) + \
		5 * sum(small[i][1] + small[i][3] + small[i][5] + small[i][7] for i in range(9)) + \
		9 * sum(small[i][4] for i in range(9)) + \
		25 * (
			7 * (large[0] + large[2] + large[6] + large[8]) + \
			5 * (large[1] + large[3] + large[5] + large[7]) + \
			9 * large[4]
		)
	return eval + (sq_score if side else -sq_score)

def alpha_beta(board, side, depth, alpha, beta):
	move_list = move_gen(board)
	if len(move_list) == 0:
		us, them = (1, -1) if side else (-1, 1)
		large_lines = [board[1][line] for line in LINES]
		if any(x == [us, us, us] for x in large_lines):
			return 10000000 + depth - SEARCHING_DEPTH, []
		elif any(x == [them, them, them] for x in large_lines):
			return -10000000 - depth + SEARCHING_DEPTH, []
		else:
			return 0, []
	if depth == 0:
		return evaluate(board, side), []
	pv = []
	for move in move_list:
		eval, line = alpha_beta(play_move(board, move, side), not side, depth - 1, -beta, -alpha)
		eval = -eval
		line = [move] + line
		if eval >= beta:
			return beta, line
		elif eval > alpha:
			alpha = eval
			pv = line
	return alpha, pv

def print_board(board):
	small, large, zone = board
	small, large = [[sq for sq in zone] for zone in small], [sq for sq in large]
	for i in range(9):
		for j in range(9):
			small[i][j] = 'X' if small[i][j] == 1 else 'O' if small[i][j] == -1 else '.'
		large[i] = 'X' if large[i] == 1 else 'O' if large[i] == -1 else '.'
	print('---+---+---')
	for i in range(0, 81, 27):
		big_row = small[i//9 : i//9 + 3]
		for j in range(0, 9, 3):
			print('|'.join([''.join(grid[j:j+3]) for grid in big_row]))
		print('---+---+---')
	for i in range(3):
		print(''.join(large[3*i:3*i+3]))
	print('ZONE:', 'ANY' if zone == 9 else SQUARE_ARRAY[zone].upper())

def move_string(move):
	return SQUARE_ARRAY[move // 9] + '/' + SQUARE_ARRAY[move % 9]

def eval_string(eval):
	if eval <= SEARCHING_DEPTH - 10000000:
		return 'L' + str(eval + 10000000)
	elif eval >= 10000000 - SEARCHING_DEPTH:
		return 'W' + str(10000000 - eval)
	elif eval == 0:
		return 'D0'
	else:
		return '{:+d}'.format(eval)

if __name__ == '__main__':
	depth = int(input('Depth: '))
	self_start = input() == '1'
	print('Playing', ('X' if self_start else 'O'))
	SEARCHING_DEPTH = depth
	board = (
		[
			[0, 0, 0, 0, 0, 0, 0, 0, 0],
			[0, 0, 0, 0, 0, 0, 0, 0, 0],
			[0, 0, 0, 0, 0, 0, 0, 0, 0],
			[0, 0, 0, 0, 0, 0, 0, 0, 0],
			[0, 0, 0, 0, 0, 0, 0, 0, 0],
			[0, 0, 0, 0, 0, 0, 0, 0, 0],
			[0, 0, 0, 0, 0, 0, 0, 0, 0],
			[0, 0, 0, 0, 0, 0, 0, 0, 0],
			[0, 0, 0, 0, 0, 0, 0, 0, 0]
		],
		[0, 0, 0, 0, 0, 0, 0, 0, 0],
		9
	)
	player = True
	print_board(board)
	if self_start:
		s = time.perf_counter()
		eval, line = alpha_beta(board, True, SEARCHING_DEPTH, -10000000, 10000000)
		e = time.perf_counter()
		board = play_move(board, line[0], True)
		print('AI Move:', move_string(line[0]), 'PV: [' + ', '.join([move_string(mv) for mv in line]) + ']', 'Eval:', eval_string(eval), 'Time elapsed:', e-s, 's')
		player = False

	print_board(board)
	while True:
		possible_moves = move_gen(board)
		if len(possible_moves) == 0:
			print('Game over')
			input()
			break
		zone, square = input('Move: ').split('/')
		move = 9 * SQUARE_ARRAY.index(zone) + SQUARE_ARRAY.index(square)
		while move not in possible_moves:
			zone, square = input('Move: ').split('/')
			move = 9 * SQUARE_ARRAY.index(zone) + SQUARE_ARRAY.index(square)
		board = play_move(board, move, player)
		print_board(board)
		if len(move_gen(board)) == 0:
			print('Game over')
			input()
			break
		s = time.perf_counter()
		eval, line = alpha_beta(board, not player, SEARCHING_DEPTH, -10000000, 10000000)
		e = time.perf_counter()
		board = play_move(board, line[0], not player)
		print('AI Move:', move_string(line[0]), 'PV: [' + ', '.join([move_string(mv) for mv in line]) + ']', 'Eval:', eval_string(eval), 'Time elapsed:', e-s, 's')
		print_board(board)