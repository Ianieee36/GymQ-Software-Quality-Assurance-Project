# GymQ

### Software Quality Assurance Project ENSE707

Christian Cantos 23188023  
Jayden Marsh 23217931  
Lorenz Soriano 18011129


## About

GymQ is a digital queue and equipment managing app built for busy gyms. It gives gym-goers a way to formally queue for popular machines and report broken equpment.

**Features:**  
\- Digital queuing for machines/equipment.  
\- A way to report broken equipment.  
\- View current machine/equipment satus.  
\- Gym traffic and machine usage analytics.

## Development after restructuring

Run these commands from the repository root (the folder containing `GymQ.slnx`).
Install the .NET 8 SDK for the app and .NET 10 SDK for the test project.

```sh
dotnet restore GymQ.slnx
dotnet build GymQ.slnx -c Release
dotnet test GymQ.slnx -c Release --logger "trx;LogFileName=verification.trx" --results-directory TestResults
dotnet run --project src/GymQ.Desktop/GymQ.Desktop.csproj
```

- `src/GymQ.Core`: models and services.
- `src/GymQ.Desktop`: Avalonia application and views; references Core.
- `tests/GymQ.Tests`: original service tests and cross-module integration tests; references Core.
- `docs`: project documentation.

`bin`, `obj`, and `TestResults` are generated and ignored by Git. Preserve any test results needed as assessment evidence separately.
