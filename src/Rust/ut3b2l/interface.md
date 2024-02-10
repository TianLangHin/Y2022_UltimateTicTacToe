# Interface Protocol

This describes the way the CLI application version of this engine will respond to command line input.
The format is designed to facilitate a GUI communicating with this CLI application
to display changes in game state. The format of the messages described here are inspired
by the [Universal Chess Interface](https://en.wikipedia.org/wiki/Universal_Chess_Interface).

Throughout the life of the program, it will store the history of the current game
as a sequence of board and move pairs. The very first entry in this sequence will
always be the chosen starting position and a null move (represented by the `NULL_MOVE` value internally.

Currently, it only takes input when it is not thinking. This will be fixed in later versions.

Commands are categorised solely on the first word in the command.
Extra arguments are allowed to be present, but will not affect the running of the command.

Additionally, a game position is serialised as a string,
inspired by the [Forsyth-Edwards Notation](https://en.wikipedia.org/wiki/Forsyth%E2%80%93Edwards_Notation).
The format is as follows.

The position string consists of two fields.

The first field describes the small grid occupancies as 9 rows from top to bottom,
and occupancies within each row being described from left to right.
Places occupied by Player "X" is denoted by `x`, and places occupied by Player "O" is denoted by `o`.
One or more consecutive empty squares within a row is denoted by a digit from `1` to `9`,
corresponding to the number of empty squares.
Each row will pass through three zones, but these barriers do not affect the representation
of consecutive empty squares.
The occupancies of the large grid are not represented directly in the string, as it can be
calculated directly from the occupancies of all 81 smaller occupancies.

The second field describes the zone that the next player can play in, which will be either
one of the nine zones or the word "any".

For example, the string `2x6/9/9/9/9/9/9/9/9 ne` corresponds to the grid
with the following graphical representation.
```
---+---+---
..X|...|...
...|...|...
...|...|...
---+---+---
...|...|...
...|...|...
...|...|...
---+---+---
...|...|...
...|...|...
...|...|...
---+---+---
...
...
...
ZONE: NE
```

Upon startup, when the engine has finished preparing, it will send `ready` to output.

The following are commands that the engine will accept as input.

### newgame

Takes a position string as an argument.

This command starts a new game, clearing all previous game history.
The first entry in the game history from here on (until it is overwritten by another `newgame` command)
will be the position given as an argument in this command.

All responses from the engine will begin with the `newgame` keyword.

If a new game cannot be correctly started with the inputted arguments,
`invalid` is appended to the response.
* If no starting position was given, `args` is appended.
* If the starting position given was invalid, `pos` is appended.

If instead the position is valid, the game history will be updated and `ok` will be appended to the response.

### go

Takes a number as an argument.

This command starts a minimax search from the current position for the given number of plies ahead.
The engine decides for itself which side it is evaluating for, depending on how many moves have been
played in the current game history.

If an even number of moves had been played
(excluding the initial sentinel null move and starting position pair),
the engine will play for Player X.

If an odd number of moves had been played, the engine will play for Player O.

The side that the engine plays for can therefore be changed without affecting the board
by playing a "null move" using the `play` command.

All responses from the engine will begin with the `info` keyword.

If a valid search cannot be started, `error` is appended to the response.
* If no depth is given as an argument, `no depth` is appended to the response.
* If an argument has been passed that is not a valid positive integer, `invalid depth` is appended to the response.

If a valid search can be started, the search is executed, and once finished,
will output a string in the following format.
`info depth <depth> pv <moves> eval <eval> time <time>`

* `<depth>` is the depth in plies that has been searched, determined by the argument for `go`.
* `<moves>` is the principal variation, a space separated list of moves found to be "best play" by the engine.
* `<eval>` is the heuristic score given to the line of best play by the engine.
* `<time>` is the time taken to execute the search in milliseconds.

### play

Takes a move as an argument.

This command plays a move as an external player.
The move is interpreted as either Player X or O depending on the number of moves made prior
in the current game's history.

If an even number of moves had been played
(excluding the initial sentinel null move and starting position pair),
the move is played for Player X.

If an odd number of moves had been played, the move is played for Player O.

All responses from the engine will begin with the `move` keyword.

* If no move is provided or the move is of an incorrect format, `invalid` is appended to the response.
* If the move provided is of the right format but not a legal move in this position, `illegal` is appended to the response.
* If the move is a legal move or a null move (represented by the text `null`), `pos` is appnded to the response, as well as the string representing the position after the move is made.

### undo

Takes no extra arguments.

Undoes a move from the game history, restoring the current state of the game
to the state in the previous turn.

All reponses from the engine will begin with the `undo` keyword.

* If no moves had been made in this game by the time `undo` is inputted, `stackempty` is appended to the response.
* Otherwise, the most recent move in the game is undone, and `ok` is appended to the response.

### gamepos

Takes no extra arguments.

Returns the string representation of the current board in the game.

### d

Takes no extra arguments.

Outputs an ASCII art representation of the current board in the game.
This command typically will not be used by a GUI, but may be useful
for a user directly reading from the CLI console app.

### q

Takes no extra arguments.

Exits the program.