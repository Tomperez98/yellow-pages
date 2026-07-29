import jwt from "jsonwebtoken";

// ---------- Mutex ----------
// ponytail: promise-chain lock, per-account if contention matters
let lock: Promise<void> = Promise.resolve();
function withLock<T>(fn: () => T | Promise<T>): Promise<T> {
	const prev = lock;
	let release!: () => void;
	lock = new Promise<void>((res) => {
		release = res;
	});
	return prev.then(async () => {
		try {
			return await fn();
		} finally {
			release();
		}
	});
}

// ---------- JWT claims ----------
interface Claims {
	sub: string;
	role: string;
}

function parseClaims(token: string, secret: string): Claims {
	const payload = jwt.verify(token, secret, {
		algorithms: ["HS256"],
	}) as Record<string, unknown>;
	return { sub: payload.sub as string, role: payload.role as string };
}

// ---------- State ----------
type TimerStatus = "Active" | "Completed";

interface TimerItem {
	id: string;
	slug: string;
	deadline: string;
	status: TimerStatus;
}

const timers: TimerItem[] = [];

// ponytail: single interval for deadline completion
const DEADLINE_CHECK_MS = 500;
setInterval(() => {
	const now = new Date().toISOString();
	for (const t of timers) {
		if (t.status === "Active" && t.deadline < now) {
			t.status = "Completed";
		}
	}
}, DEADLINE_CHECK_MS);

// ---------- Handlers ----------
async function createTimer(
	slug: string,
	deadline: string,
	role: string,
): Promise<ResponseResult> {
	if (role !== "user") return { status: 403, error: "Not authorized" };
	if (!slug || slug.trim().length === 0)
		return { status: 400, error: "Slug cannot be empty" };

	return withLock(() => {
		if (timers.some((t) => t.slug === slug))
			return { status: 409, error: "Timer with this slug already exists" };

		const id = Bun.randomUUIDv7();
		timers.push({ id, slug, deadline, status: "Active" });
		return { status: 201, data: { TimerId: id } };
	});
}

async function getTimer(id: string, role: string): Promise<ResponseResult> {
	if (role !== "user") return { status: 403, error: "Not authorized" };

	const timer = timers.find((t) => t.id === id);
	if (!timer) return { status: 404, error: "Timer not found" };

	return { status: 200, data: { Status: timer.status } };
}

// ---------- Server ----------
const SECRET = Bun.env.JWT_SECRET || "dev-secret-at-least-128-bits-long!!";
const PORT = Number(Bun.env.PORT) || 3000;

Bun.serve({
	port: PORT,
	async fetch(req) {
		const url = new URL(req.url);

		if (url.pathname === "/rpc/reset") {
			timers.length = 0;
			return new Response(null, { status: 204 });
		}

		const route = ROUTES[url.pathname];
		if (!route) return new Response("Not Found", { status: 404 });

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

		let body: Record<string, unknown>;
		try {
			body = await req.json();
		} catch {
			body = {};
		}

		const result = await route.handler(body, claims.role);
		return respond(result);
	},
});

type ResponseResult =
	| { status: number; data: unknown }
	| { status: number; error: string };

type Handler = (
	body: Record<string, unknown>,
	role: string,
) => Promise<ResponseResult>;

function respond(result: ResponseResult): Response {
	const payload = "error" in result ? { error: result.error } : result.data;
	return new Response(JSON.stringify(payload), {
		status: result.status,
		headers: { "Content-Type": "application/json" },
	});
}

const ROUTES: Record<string, { handler: Handler }> = {
	"/rpc/create_timer": {
		handler: (body, role) =>
			createTimer(body.slug as string, body.deadline as string, role),
	},
	"/rpc/get_timer": {
		handler: (body, role) => getTimer(body.id as string, role),
	},
};

console.log(`Server running on http://localhost:${PORT}`);
console.log(`JWT secret: ${SECRET}`);
