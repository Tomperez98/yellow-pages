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
	const now = Date.now();
	for (const t of timers) {
		if (t.status === "Active" && new Date(t.deadline).getTime() < now) {
			t.status = "Completed";
		}
	}
}, DEADLINE_CHECK_MS);

// ---------- Handlers ----------
async function createTimer(
	slug: string,
	deadline: string,
): Promise<ResponseResult> {
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

async function getTimer(id: string): Promise<ResponseResult> {
	const timer = timers.find((t) => t.id === id);
	if (!timer) return { status: 404, error: "Timer not found" };

	return { status: 200, data: { Status: timer.status } };
}

// ---------- Server ----------
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

		let body: Record<string, unknown>;
		try {
			body = await req.json();
		} catch {
			body = {};
		}

		const result = await route.handler(body);
		return respond(result);
	},
});

type ResponseResult =
	| { status: number; data: unknown }
	| { status: number; error: string };

type Handler = (body: Record<string, unknown>) => Promise<ResponseResult>;

function respond(result: ResponseResult): Response {
	const payload = "error" in result ? { error: result.error } : result.data;
	return new Response(JSON.stringify(payload), {
		status: result.status,
		headers: { "Content-Type": "application/json" },
	});
}

const ROUTES: Record<string, { handler: Handler }> = {
	"/rpc/create_timer": {
		handler: (body) =>
			createTimer(body.slug as string, body.deadline as string),
	},
	"/rpc/get_timer": {
		handler: (body) => getTimer(body.id as string),
	},
};

console.log(`Server running on http://localhost:${PORT}`);
