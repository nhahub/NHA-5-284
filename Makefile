.PHONY: lint test build-image up down

lint:
	dotnet build Backend/BankingApi.sln /p:TreatWarningsAsErrors=true

test: lint
	dotnet test Backend/BankingApi.sln --no-build

build-image:
	docker build -t banking-api:local .

up:
	docker compose up -d --build

down:
	docker compose down -v

