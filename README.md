# Ultimate Tic-Tac-Toe

This is a basic proof of concept of utilising bitboards together with alpha-beta pruning minimax to create a rudimentary engine that can play a game against a player in the console.

To ensure the game is non-trivial, rather than the traditional 3x3 or 4x4 Tic-Tac-Toe games, this engine attempts to play the game of [Ultimate Tic-Tac-Toe](https://en.wikipedia.org/wiki/Ultimate_tic-tac-toe).

Upon startup, program is given the ply depth it is to search at each iteration,
and also which side it is playing.

The board is displayed using ASCII art in the form of two grids and a zone indicator. The first grid shows the occupancies of each of the 81 smaller cells, while the second grid below it shows the larger occupancies of each of the 9 larger boards.
An occupancy is represented by one of the three following symbols.
* `X` indicates an occupancy by Player 1,
* `O` indicates an occupancy by Player 2,
* `.` indicates a vacancy.

Cells within a 3x3 grid are represented using one of 9 names, derived from the abbreviations of the directions on the 8-wind compass rose, depicted graphically as follows.
```
+----+----+----+
| nw | n  | ne |
+----+----+----+
| w  | c  | e  |
+----+----+----+
| sw | s  | se |
+----+----+----+
```

Referring to the cells within the board in its totality is done by indicating the larger 3x3 grid followed by the position within that grid, separated by a `/`. For example, `nw/ne` represents the following spot marked with an `X`.

```
+---+---+---+
|..X|...|...|
|...|...|...|
|...|...|...|
+---+---+---+
|...|...|...|
|...|...|...|
|...|...|...|
+---+---+---+
|...|...|...|
|...|...|...|
|...|...|...|
+---+---+---+
```

Additionally, when the engine plays a move, it also displays the following information.
* The move played by the engine,
* The principal variation, that is, what it considers to be "best play",
* The evaluation of the current position from the perspective of the engine,
  in the form of a symbol or character followed by an integer.
    * `W` indicates a guaranteed win has been found for the engine, with the following integer representing the number of moves it will occur in with "best play" from both sides.
    * `L` indicates the engine is guaranteed to lose, with the following integer representing the number of moves it will occur in with "best play" from both sides.
    * `D` indicates the engine evaluates the position to be completely drawn, and is always followed by a `0`.
    * `+` indicates the engine evaluates the position to be better for the engine, with the following integer representing the evaluted heuristic score.
    * `-` indicates the engine evaluates the position to be worse for the engine, with the following integer representing the evaluted heuristic score.
* The time taken to calculate that particular move, in milliseconds.

### Example Output
The following output is an example from when the engine is playing as `O` (the second player), and searching at a depth of 8 ply.
```
AI Move: n/se PV: [n/se, ne/c, c/e, e/w, w/nw, sw/c, s/e, e/nw] Eval: +91 Time elapsed: 3551 ms
---+---+---
OOO|.O.|.XO
.X.|XX.|...
X..|OOO|.XO
---+---+---
.XX|X.X|...
O.X|OO.|.O.
OO.|...|...
---+---+---
XX.|.X.|X..
O..|X..|X..
O.X|..O|X.O
---+---+---
OO.
...
..X
ZONE: ANY
```

Here, the engine has just placed the `O` token in the right-bottom cell of the middle-upper 3x3 grid of the board, and evalutes the position to be better for itself, after calculating 8 total moves (4 per player) ahead.

## Implementation

This project is implemented in multiple languages to showcase different ways this goal can be achieved, and will be in folders of their own.