using UnityEngine;
using System;

/// <summary>
/// Central game manager responsible for coordinating game state, 
/// time management, and system communication.
/// Implements IGameStateManager for dependency injection and testability.
/// </summary>
public class GameManager : MonoBehaviour, IGameStateManager
{
    [Header("Configuration")]
    [SerializeField] private bool startGameOnAwake = false;
    [SerializeField] private bool allowPause = true;

    [Header("Player Management")]
    [SerializeField] private GameObject player;
    [SerializeField] private Transform playerSpawnPoint;

    private GameState currentState = GameState.Menu;
    private GameObject currentPlayer;
    private IPlayerMovement playerMovement;
    private PlayerInputHandler playerInputHandler;
    private PlayerAim playerAim;
    private PlayerHealthSystem playerHealth;

    /// <summary>
    /// Current state of the game.
    /// </summary>
    public GameState CurrentState
    {
        get => currentState;
        private set
        {
            if (currentState != value)
            {
                GameState previousState = currentState;
                currentState = value;
                OnGameStateChanged?.Invoke(previousState, currentState);
            }
        }
    }

    /// <summary>
    /// Event raised when game state changes. Provides previous and new state.
    /// </summary>
    public event Action<GameState, GameState> OnGameStateChanged;

    private void Awake()
    {
        Initialize();
    }

    /// <summary>
    /// Starts the dungeon ambience. In Start rather than Awake so it runs after
    /// AudioRuntime's AfterSceneLoad bootstrap, and on this component because it lives only
    /// in the dungeon scene — every route in (Play, a loaded save, the death screen's
    /// restart) reloads that scene and so re-runs this.
    /// </summary>
    private void Start()
    {
        AudioService.PlayMusic(SoundId.MusicDungeon);
    }

    /// <summary>
    /// Stops it on the way out, wherever the exit is. AudioRuntime is DontDestroyOnLoad, so
    /// a track nobody stops would play on under the main menu.
    /// </summary>
    private void OnDestroy()
    {
        AudioService.StopMusic();
    }

    private void Initialize()
    {
        if (startGameOnAwake)
        {
            StartGame();
        }
        else
        {
            CurrentState = GameState.Menu;
        }
    }

    /// <summary>
    /// Transitions the game to the playing state and initializes player if needed.
    /// </summary>
    public void StartGame()
    {
        if (CurrentState == GameState.Playing) return;

        CurrentState = GameState.Playing;
        Time.timeScale = 1f;

        InitializePlayer();
    }

    /// <summary>
    /// Pauses the game by setting time scale to zero.
    /// </summary>
    public void PauseGame()
    {
        if (CurrentState != GameState.Playing || !allowPause) return;

        CurrentState = GameState.Paused;
        Time.timeScale = 0f;
    }

    /// <summary>
    /// Resumes the game by restoring normal time scale.
    /// </summary>
    public void ResumeGame()
    {
        if (CurrentState != GameState.Paused) return;

        CurrentState = GameState.Playing;
        Time.timeScale = 1f;
    }

    /// <summary>
    /// Toggles between paused and playing states.
    /// </summary>
    public void TogglePause()
    {
        // Victory sits alongside GameOver here for the same reason: the run is over, and
        // pausing something that has already ended only puts the pause menu over the end
        // screen.
        if (!allowPause || CurrentState == GameState.Menu ||
            CurrentState == GameState.GameOver || CurrentState == GameState.Victory)
            return;

        if (CurrentState == GameState.Paused)
        {
            ResumeGame();
        }
        else if (CurrentState == GameState.Playing)
        {
            PauseGame();
        }
    }

    /// <summary>
    /// Transitions the game to the game over state.
    /// </summary>
    public void GameOver()
    {
        if (CurrentState == GameState.GameOver) return;

        CurrentState = GameState.GameOver;
        Time.timeScale = 1f;
    }

    /// <summary>
    /// Ends the run as a win. Called when the player reaches the dungeon exit — see
    /// <see cref="DungeonExit"/>.
    ///
    /// Time is left running rather than frozen, matching <see cref="GameOver"/>: whichever
    /// screen the UI puts up decides for itself whether the world behind it should keep
    /// moving, and a manager that has already stopped time takes that choice away.
    /// </summary>
    public void WinGame()
    {
        if (CurrentState == GameState.Victory) return;

        CurrentState = GameState.Victory;

        var victoryUI = FindFirstObjectByType<VictoryScreenUI>(FindObjectsInactive.Include);
        if (victoryUI != null)
        {
            victoryUI.Show();
        }
    }

    /// <summary>
    /// Restarts the game by resetting state and teleporting player to spawn.
    /// </summary>
    public void RestartGame()
    {
        Time.timeScale = 1f;
        CurrentState = GameState.Menu;

        currentPlayer = null;
        CachePlayerComponents();

        StartGame();
    }

    /// <summary>
    /// Returns to the main menu and teleports player back to spawn point.
    /// </summary>
    public void ReturnToMenu()
    {
        Time.timeScale = 1f;
        CurrentState = GameState.Menu;

        TeleportPlayerToSpawn();
    }

    /// <summary>
    /// Determines if the game is currently in the playing state.
    /// </summary>
    public bool IsPlaying()
    {
        return CurrentState == GameState.Playing;
    }

    /// <summary>
    /// Determines if the game is currently paused.
    /// </summary>
    public bool IsPaused()
    {
        return CurrentState == GameState.Paused;
    }

    /// <summary>
    /// Determines if the game is in the menu state.
    /// </summary>
    public bool IsInMenu()
    {
        return CurrentState == GameState.Menu;
    }

    /// <summary>
    /// Determines if the game is in the game over state.
    /// </summary>
    public bool IsGameOver()
    {
        return CurrentState == GameState.GameOver;
    }

    /// <summary>
    /// Determines if the run was completed successfully.
    /// </summary>
    public bool IsVictory()
    {
        return CurrentState == GameState.Victory;
    }

    /// <summary>
    /// Initializes player reference and teleports to spawn point.
    /// </summary>
    private void InitializePlayer()
    {
        if (currentPlayer != null) return;

        if (player == null)
        {
            Debug.LogWarning($"{nameof(GameManager)}: Player is not assigned in inspector.");
            return;
        }

        currentPlayer = player;
        CachePlayerComponents();
        TeleportPlayerToSpawn();
    }

    /// <summary>
    /// Teleports the player to the configured spawn point.
    /// </summary>
    private void TeleportPlayerToSpawn()
    {
        if (currentPlayer == null) return;

        Vector3 spawnPosition = playerSpawnPoint != null 
            ? playerSpawnPoint.position 
            : Vector3.zero;

        currentPlayer.transform.position = spawnPosition;
        
        Rigidbody2D rb = currentPlayer.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
        }
    }

    /// <summary>
    /// Caches references to player components and injects dependencies.
    /// </summary>
    private void CachePlayerComponents()
    {
        if (currentPlayer == null) return;

        playerInputHandler = currentPlayer.GetComponent<PlayerInputHandler>();
        playerMovement = currentPlayer.GetComponent<IPlayerMovement>();
        playerAim = currentPlayer.GetComponent<PlayerAim>();
        playerHealth = currentPlayer.GetComponent<PlayerHealthSystem>();
        
        InjectDependencies();
    }

    /// <summary>
    /// Injects IGameStateManager dependency into player components.
    /// </summary>
    private void InjectDependencies()
    {
        if (playerInputHandler != null)
        {
            playerInputHandler.SetGameStateManager(this);
        }

        if (playerAim != null)
        {
            playerAim.SetGameStateManager(this);
        }
    }

    /// <summary>
    /// Sets the current player instance and caches its components.
    /// </summary>
    /// <param name="player">The player GameObject instance.</param>
    public void SetPlayer(GameObject player)
    {
        currentPlayer = player;
        CachePlayerComponents();
    }

    /// <summary>
    /// Gets the current player GameObject instance.
    /// </summary>
    /// <returns>The player GameObject, or null if not spawned.</returns>
    public GameObject GetPlayer()
    {
        return currentPlayer;
    }

    /// <summary>
    /// Gets the PlayerMovement component from the current player.
    /// </summary>
    /// <returns>The IPlayerMovement interface, or null if not available.</returns>
    public IPlayerMovement GetPlayerMovement()
    {
        return playerMovement;
    }

    /// <summary>
    /// Gets the PlayerAim component from the current player.
    /// </summary>
    /// <returns>The PlayerAim component, or null if not available.</returns>
    public PlayerAim GetPlayerAim()
    {
        return playerAim;
    }

    /// <summary>
    /// Gets the PlayerInputHandler component from the current player.
    /// </summary>
    /// <returns>The PlayerInputHandler component, or null if not available.</returns>
    public PlayerInputHandler GetPlayerInputHandler()
    {
        return playerInputHandler;
    }
}
