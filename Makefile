.PHONY: lint test build-image up down

lint:
	dotnet build Backend/BankingApi.sln --nologo -v quiet -clp:ErrorsOnly /p:TreatWarningsAsErrors=true

test-fast:
	dotnet test Backend/BankingApi.sln --nologo -v quiet -clp:ErrorsOnly --logger "console;verbosity=minimal"

test: lint
	dotnet test Backend/BankingApi.sln --no-build --nologo -v quiet --logger "console;verbosity=minimal"

build-image:
	docker build -t banking-api:local .

up:
	docker compose up -d --build

down:
	docker compose down -v

