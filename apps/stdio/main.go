package main

import (
	"bufio"
	"encoding/json"
	"fmt"
	"os"

	"github.com/google/uuid"
)

// --- Protocol envelope ---

type Request struct {
	Type    string          `json:"type"`
	Payload json.RawMessage `json:"payload,omitempty"`
}

type Response struct {
	Status int             `json:"status"`
	Result json.RawMessage `json:"result,omitempty"`
	Error  string          `json:"error,omitempty"`
}

// --- Operation-specific payloads ---

type Claims struct {
	Sub     string          `json:"sub"`
	Role    string          `json:"role"`
	OrgID   string          `json:"org_id"`
	OrgRole string          `json:"org_role"`
	Orgs    []OrgMembership `json:"orgs"`
}

type OrgMembership struct {
	OrgID string `json:"OrgId"`
	Role  string `json:"Role"`
}

type CreatePayload struct {
	Code   string `json:"code"`
	Claims Claims `json:"claims"`
}

type UpdatePayload struct {
	ID     string `json:"id"`
	Code   string `json:"code"`
	Claims Claims `json:"claims"`
}

type DeletePayload struct {
	ID     string `json:"id"`
	Claims Claims `json:"claims"`
}

// --- State ---

type Country struct {
	ID   string `json:"id"`
	Code string `json:"code"`
}

var countries []Country

// --- Main ---

func main() {
	scanner := bufio.NewScanner(os.Stdin)
	scanner.Buffer(make([]byte, 0, bufio.MaxScanTokenSize), bufio.MaxScanTokenSize)

	for scanner.Scan() {
		var req Request
		if err := json.Unmarshal(scanner.Bytes(), &req); err != nil {
			write(Response{Status: 400, Error: fmt.Sprintf("invalid request: %v", err)})
			continue
		}
		write(handle(req))
	}
}

// --- Router ---

func handle(req Request) Response {
	switch req.Type {
	case "reset":
		countries = nil
		return Response{Status: 204}

	case "create_country":
		var p CreatePayload
		if err := json.Unmarshal(req.Payload, &p); err != nil {
			return Response{Status: 400, Error: fmt.Sprintf("invalid payload: %v", err)}
		}
		return createCountry(p)

	case "update_country":
		var p UpdatePayload
		if err := json.Unmarshal(req.Payload, &p); err != nil {
			return Response{Status: 400, Error: fmt.Sprintf("invalid payload: %v", err)}
		}
		return updateCountry(p)

	case "delete_country":
		var p DeletePayload
		if err := json.Unmarshal(req.Payload, &p); err != nil {
			return Response{Status: 400, Error: fmt.Sprintf("invalid payload: %v", err)}
		}
		return deleteCountry(p)

	default:
		return Response{Status: 400, Error: fmt.Sprintf("unknown type: %s", req.Type)}
	}
}

// --- Handlers ---

func createCountry(p CreatePayload) Response {
	if p.Claims.Role != "admin" {
		return Response{Status: 403, Error: "Not authorized"}
	}
	if p.Code == "" {
		return Response{Status: 400, Error: "Code cannot be empty"}
	}
	for _, c := range countries {
		if c.Code == p.Code {
			return Response{Status: 409, Error: "Country already exists"}
		}
	}
	id := uuid.Must(uuid.NewV7()).String()
	countries = append(countries, Country{ID: id, Code: p.Code})
	result, _ := json.Marshal(map[string]string{"CountryId": id})
	return Response{Status: 201, Result: result}
}

func updateCountry(p UpdatePayload) Response {
	if p.Claims.Role != "admin" {
		return Response{Status: 403, Error: "Not authorized"}
	}
	if p.Code == "" {
		return Response{Status: 400, Error: "Code cannot be empty"}
	}
	idx := -1
	for i, c := range countries {
		if c.ID == p.ID {
			idx = i
			break
		}
	}
	if idx == -1 {
		return Response{Status: 404, Error: "Country not found"}
	}
	for _, c := range countries {
		if c.ID != p.ID && c.Code == p.Code {
			return Response{Status: 409, Error: "Another country already has this code"}
		}
	}
	countries[idx].Code = p.Code
	result, _ := json.Marshal(map[string]any{})
	return Response{Status: 200, Result: result}
}

func deleteCountry(p DeletePayload) Response {
	if p.Claims.Role != "admin" {
		return Response{Status: 403, Error: "Not authorized"}
	}
	idx := -1
	for i, c := range countries {
		if c.ID == p.ID {
			idx = i
			break
		}
	}
	if idx == -1 {
		return Response{Status: 404, Error: "Country not found"}
	}
	countries = append(countries[:idx], countries[idx+1:]...)
	result, _ := json.Marshal(map[string]any{})
	return Response{Status: 200, Result: result}
}

func write(resp Response) {
	b, _ := json.Marshal(resp)
	fmt.Println(string(b))
}
