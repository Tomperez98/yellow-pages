package main

import (
	"bufio"
	"encoding/json"
	"fmt"
	"os"
	"time"

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
	Sub  string `json:"sub"`
	Role string `json:"role"`
}

type CreatePayload struct {
	Slug     string `json:"slug"`
	Deadline string `json:"deadline"`
	Claims   Claims `json:"claims"`
}

type GetPayload struct {
	ID     string `json:"id"`
	Claims Claims `json:"claims"`
}

// --- State ---

type TimerItem struct {
	ID       string `json:"id"`
	Slug     string `json:"slug"`
	Deadline string `json:"deadline"`
	Status   string `json:"status"`
}

var timers []TimerItem

// ponytail: single goroutine for deadline completion
func init() {
	go func() {
		for {
			time.Sleep(500 * time.Millisecond)
			now := time.Now().UTC().Format(time.RFC3339Nano)
			for i := range timers {
				if timers[i].Status == "Active" && timers[i].Deadline < now {
					timers[i].Status = "Completed"
				}
			}
		}
	}()
}

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
		timers = nil
		return Response{Status: 204}

	case "create_timer":
		var p CreatePayload
		if err := json.Unmarshal(req.Payload, &p); err != nil {
			return Response{Status: 400, Error: fmt.Sprintf("invalid payload: %v", err)}
		}
		return createTimer(p)

	case "get_timer":
		var p GetPayload
		if err := json.Unmarshal(req.Payload, &p); err != nil {
			return Response{Status: 400, Error: fmt.Sprintf("invalid payload: %v", err)}
		}
		return getTimer(p)

	default:
		return Response{Status: 400, Error: fmt.Sprintf("unknown type: %s", req.Type)}
	}
}

// --- Handlers ---

func createTimer(p CreatePayload) Response {
	if p.Claims.Role != "user" {
		return Response{Status: 403, Error: "Not authorized"}
	}
	if p.Slug == "" {
		return Response{Status: 400, Error: "Slug cannot be empty"}
	}
	for _, t := range timers {
		if t.Slug == p.Slug {
			return Response{Status: 409, Error: "Timer with this slug already exists"}
		}
	}
	id := uuid.Must(uuid.NewV7()).String()
	timers = append(timers, TimerItem{ID: id, Slug: p.Slug, Deadline: p.Deadline, Status: "Active"})
	result, _ := json.Marshal(map[string]string{"TimerId": id})
	return Response{Status: 201, Result: result}
}

func getTimer(p GetPayload) Response {
	if p.Claims.Role != "user" {
		return Response{Status: 403, Error: "Not authorized"}
	}
	for i := range timers {
		if timers[i].ID == p.ID {
			result, _ := json.Marshal(map[string]string{"Status": timers[i].Status})
			return Response{Status: 200, Result: result}
		}
	}
	return Response{Status: 404, Error: "Timer not found"}
}

func write(resp Response) {
	b, _ := json.Marshal(resp)
	fmt.Println(string(b))
}
