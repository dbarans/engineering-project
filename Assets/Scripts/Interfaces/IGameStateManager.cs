using System;

/// <summary>
/// Defines the contract for game state management operations.
/// Enables dependency injection and improves testability.
/// </summary>
public interface IGameStateManager
{
    /// <summary>
    /// Current state of the game.
    /// </summary>
    GameState CurrentState { get; }

    /// <summary>
    /// Event raised when game state changes.
    /// </summary>
    event Action<GameState, GameState> OnGameStateChanged;

    /// <summary>
    /// Transitions the game to the playing state.
    /// </summary>
    void StartGame();

    /// <summary>
    /// Pauses the game if currently playing.
    /// </summary>
    void PauseGame();

    /// <summary>
    /// Resumes the game if currently paused.
    /// </summary>
    void ResumeGame();

    /// <summary>
    /// Toggles between paused and playing states.
    /// </summary>
    void TogglePause();

    /// <summary>
    /// Transitions the game to the game over state.
    /// </summary>
    void GameOver();

    /// <summary>
    /// Transitions the game to the victory state: the run was completed rather than lost.
    /// </summary>
    void WinGame();

    /// <summary>
    /// Restarts the game by resetting state and starting fresh.
    /// </summary>
    void RestartGame();

    /// <summary>
    /// Returns to the main menu state.
    /// </summary>
    void ReturnToMenu();

    /// <summary>
    /// Determines if the game is currently in the playing state.
    /// </summary>
    /// <returns>True if playing, false otherwise.</returns>
    bool IsPlaying();

    /// <summary>
    /// Determines if the game is currently paused.
    /// </summary>
    /// <returns>True if paused, false otherwise.</returns>
    bool IsPaused();

    /// <summary>
    /// Determines if the game is in the menu state.
    /// </summary>
    /// <returns>True if in menu, false otherwise.</returns>
    bool IsInMenu();

    /// <summary>
    /// Determines if the game is in the game over state.
    /// </summary>
    /// <returns>True if game over, false otherwise.</returns>
    bool IsGameOver();

    /// <summary>
    /// Determines if the run was completed successfully.
    /// </summary>
    /// <returns>True if the game is in the victory state, false otherwise.</returns>
    bool IsVictory();
}

/// <summary>
/// Represents the possible states of the game.
/// </summary>
public enum GameState
{
    Menu,
    Playing,
    Paused,
    GameOver,

    /// <summary>
    /// The run was finished: the player reached the dungeon exit. Kept apart from
    /// <see cref="GameOver"/> even though both end the run, because everything that
    /// listens for the end wants to tell the two apart — the screen it shows, the music it
    /// plays, and whether the save is a corpse or a completed run.
    /// </summary>
    Victory
}

