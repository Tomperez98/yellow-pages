import jwt from "jsonwebtoken";

// ---------- JWT claims ----------
interface Claims {
	sub: string;
	role: string;
	org_id: string;
	org_role: string;
	orgs: { OrgId: string; Role: string }[];
}

function parseClaims(token: string, secret: string): Claims {
	const payload = jwt.verify(token, secret, {
		algorithms: ["HS256"],
	}) as Record<string, unknown>;
	return {
		sub: payload.sub as string,
		role: payload.role as string,
		org_id: payload.org_id as string,
		org_role: payload.org_role as string,
		orgs: JSON.parse(payload.orgs as string),
	};
}

// ---------- State ----------
interface Country {
	id: string;
	code: string;
}

const countries: Country[] = [];

// ---------- Handlers ----------
function createCountry(code: string, role: string): ResponseResult {
	if (role !== "admin") return { status: 403, error: "Not authorized" };
	if (!code || code.trim().length === 0)
		return { status: 400, error: "Code cannot be empty" };
	if (countries.some((c) => c.code === code))
		return { status: 409, error: "Country already exists" };

	const id = Bun.randomUUIDv7();
	countries.push({ id, code });
	return { status: 201, data: { CountryId: id } };
}

function updateCountry(id: string, code: string, role: string): ResponseResult {
	if (role !== "admin") return { status: 403, error: "Not authorized" };
	if (!code || code.trim().length === 0)
		return { status: 400, error: "Code cannot be empty" };

	const country = countries.find((c) => c.id === id);
	if (!country) return { status: 404, error: "Country not found" };

	if (countries.some((c) => c.id !== id && c.code === code))
		return { status: 409, error: "Another country already has this code" };

	country.code = code;
	return { status: 200, data: {} };
}

function deleteCountry(id: string, role: string): ResponseResult {
	if (role !== "admin") return { status: 403, error: "Not authorized" };

	const idx = countries.findIndex((c) => c.id === id);
	if (idx === -1) return { status: 404, error: "Country not found" };

	countries.splice(idx, 1);
	return { status: 200, data: {} };
}

// ---------- Server ----------
const SECRET = Bun.env.JWT_SECRET || "dev-secret-at-least-128-bits-long!!";
const PORT = Number(Bun.env.PORT) || 3000;

Bun.serve({
	port: PORT,
	async fetch(req) {
		const url = new URL(req.url);

		// Reset endpoint: no auth needed, handle before route lookup
		if (url.pathname === "/rpc/reset") {
			countries.length = 0;
			return new Response(null, { status: 204 });
		}

		// Route
		const route = ROUTES[url.pathname];
		if (!route) return new Response("Not Found", { status: 404 });

		// Auth
		const auth = req.headers.get("authorization");
		if (!auth?.startsWith("Bearer ")) {
			return respond({ status: 403, error: "Not authorized" });
		}

		let claims: Claims;
		try {
			claims = parseClaims(auth.slice(7), SECRET);
		} catch {
			return respond({ status: 403, error: "Not authorized" });
		}

		// Parse body
		let body: Record<string, unknown>;
		try {
			body = await req.json();
		} catch {
			body = {};
		}

		const result = route.handler(body, claims.role);
		return respond(result);
	},
});

type ResponseResult =
	| { status: number; data: unknown }
	| { status: number; error: string };

type Handler = (body: Record<string, unknown>, role: string) => ResponseResult;

function respond(result: ResponseResult): Response {
	const payload = "error" in result ? { error: result.error } : result.data;
	return new Response(JSON.stringify(payload), {
		status: result.status,
		headers: { "Content-Type": "application/json" },
	});
}

const ROUTES: Record<string, { handler: Handler }> = {
	"/rpc/create_country": {
		handler: (body, role) => createCountry(body.code as string, role),
	},
	"/rpc/update_country": {
		handler: (body, role) =>
			updateCountry(body.id as string, body.code as string, role),
	},
	"/rpc/delete_country": {
		handler: (body, role) => deleteCountry(body.id as string, role),
	},
};

console.log(`Server running on http://localhost:${PORT}`);
console.log(`JWT secret: ${SECRET}`);
