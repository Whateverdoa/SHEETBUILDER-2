SHELL := /bin/bash
BACKEND_DIR := ConsoleApp1_vdp_sheetbuilder
BACKEND_PROJECT := $(BACKEND_DIR)/ConsoleApp1_vdp_sheetbuilder/ConsoleApp1_vdp_sheetbuilder.csproj
FRONTEND_DIR := sheetbuilder-frontend

.PHONY: backend-test backend-publish frontend-install frontend-build frontend-test frontend-lint verify docker-build docker-up clean

backend-test:
	cd $(BACKEND_DIR) && dotnet test

backend-publish:
	cd $(BACKEND_DIR) && dotnet publish $(BACKEND_PROJECT) -c Release -o ./publish

frontend-install:
	cd $(FRONTEND_DIR) && npm install

frontend-build:
	cd $(FRONTEND_DIR) && npm run build

frontend-test:
	cd $(FRONTEND_DIR) && npm run test:run

frontend-lint:
	cd $(FRONTEND_DIR) && npm run lint

verify: backend-test frontend-lint frontend-test frontend-build

docker-build:
	cd $(BACKEND_DIR)/ConsoleApp1_vdp_sheetbuilder && docker build -t sheetbuilder-api .

docker-up:
	cd $(BACKEND_DIR)/ConsoleApp1_vdp_sheetbuilder && docker-compose up --build

clean:
	rm -rf $(BACKEND_DIR)/ConsoleApp1_vdp_sheetbuilder/bin \
		$(BACKEND_DIR)/ConsoleApp1_vdp_sheetbuilder/obj \
		$(BACKEND_DIR)/ConsoleApp1_vdp_sheetbuilder.Tests/bin \
		$(BACKEND_DIR)/ConsoleApp1_vdp_sheetbuilder.Tests/obj \
		$(FRONTEND_DIR)/dist \
		$(FRONTEND_DIR)/node_modules/.cache
