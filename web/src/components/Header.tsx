import { Link } from '@tanstack/react-router'

export default function Header() {
  return (
    <header className="border-b border-neutral-200">
      <div className="mx-auto flex h-16 w-full max-w-7xl items-center justify-between px-4 sm:px-6 lg:px-8">
        <Link
          to="/"
          className="text-sm font-semibold tracking-wide text-neutral-900"
        >
          ImageProcessor
        </Link>
        <p className="text-xs text-neutral-500">
          TanStack Start + Cloudflare UI shell
        </p>
      </div>
    </header>
  )
}
