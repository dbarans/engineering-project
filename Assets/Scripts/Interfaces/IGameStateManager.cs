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
}

/// <summary>
/// Represents the possible states of the game.
/// </summary>
public enum GameState
{
    Menu,
    Playing,
    Paused,
    GameOver
}

