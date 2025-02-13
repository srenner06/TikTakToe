﻿using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using TicTacToe.Lib.Enums;
using TicTacToe.Lib.Models;
using Utils.Extensions;

namespace TicTacToe.Lib.BoardHandlers;

public sealed class RemoteBoardHandler : BoardHandler, IDisposable
{
	public record RemoteResult(Player Winner, Player MyPlayer, bool OpponentLeft = false, bool Canceled = false) : Result(Winner);

	public event EventHandler? JoinedMatchmaking;
	public event EventHandler? FoundOpponent;
	public Player MyPlayer { get; private set; } = Player.NoOne;

	private readonly HubConnection _hubConnection;
	private string _gameId = "";
	private bool _disposedValue;

	public RemoteBoardHandler(string hubUrl)
	{
		_hubConnection = new HubConnectionBuilder()
			.WithUrl(hubUrl)
			.AddJsonProtocol(options =>
			{
				options.PayloadSerializerOptions.IgnoreReadOnlyFields = false;
				options.PayloadSerializerOptions.IncludeFields = true;
				options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
			})
			.Build();

		_hubConnection.On<Player[]>("UpdateGameState", OnUpdateGameState);
		_hubConnection.On<Player>("GameFinished", OnGameFinished);
		_hubConnection.On<string, Player, Player>("StartGame", OnStartGame);
		_hubConnection.On("OpponentLeft", () =>
		{
			RaiseFinished(new RemoteResult(Player.NoOne, MyPlayer, true, false));
		});
	}

	private void OnStartGame(string gameId, Player myPlayer, Player startingPlayer)
	{
		_board = new();
		_gameId = gameId;
		MyPlayer = myPlayer;
		NextTurn = startingPlayer;
		IsPlaying = true;
		FoundOpponent?.Invoke(this, EventArgs.Empty);
	}
	private void OnGameFinished(Player winner)
	{
		IsPlaying = false;
		NextTurn = Player.NoOne;
		_hubConnection.StopAsync().AwaitSync();
		RaiseFinished(new RemoteResult(winner, MyPlayer));
	}
	private void OnUpdateGameState(Player[] state)
	{
		_board = new Board(state);
		NextTurn = _board.GetFreeFields().Count() % 2 == 1 ? Player.Player1 : Player.Player2;
		RaiseUpdatedBoard();
	}

	public override void Start(Player startPlayer)
	{
		IsPlaying = false;
		_hubConnection.StartAsync().AwaitSync();
		JoinedMatchmaking?.Invoke(this, EventArgs.Empty);
		_ = _hubConnection.InvokeAsync("JoinMatchmaking");
	}
	public async Task LeaveMatchmakingAsync()
	{
		await _hubConnection.InvokeAsync("LeaveMatchmaking");
		RaiseFinished(new RemoteResult(Player.NoOne, MyPlayer, false, true));
	}

	public override bool MakeMove(Move move)
	{
		move = new Move(MyPlayer, move.Field);
		if (ValidMove(move))
		{
			_hubConnection.InvokeAsync("MakeMove", _gameId, move.Field).AwaitSync();
			return true;
		}

		return false;
	}

	private async void DisposeAsync(bool disposing)
	{
		if (!_disposedValue)
		{
			if (disposing)
			{
				try
				{
					if (_hubConnection.State == HubConnectionState.Connected)
					{
						if (IsPlaying)
							await _hubConnection.InvokeAsync("LeaveMatch", _gameId);
						else
							await _hubConnection.InvokeAsync("LeaveMatchmaking");
					}
				}
				catch { }
				finally
				{
					await _hubConnection.DisposeAsync();
				}
			}

			// TODO: Nicht verwaltete Ressourcen (nicht verwaltete Objekte) freigeben und Finalizer überschreiben
			// TODO: Große Felder auf NULL setzen
			_disposedValue = true;
		}
	}

	~RemoteBoardHandler()
	{
		// Ändern Sie diesen Code nicht. Fügen Sie Bereinigungscode in der Methode "DisposeAsync(bool disposing)" ein.
		DisposeAsync(disposing: false);
	}

	public void Dispose()
	{
		// Ändern Sie diesen Code nicht. Fügen Sie Bereinigungscode in der Methode "DisposeAsync(bool disposing)" ein.
		DisposeAsync(disposing: true);
		GC.SuppressFinalize(this);
	}

}
