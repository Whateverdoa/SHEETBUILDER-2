import { useId } from 'react'
import type { SelectProps } from '../../types'
import { cn } from '../../utils'
import { ChevronDown } from 'lucide-react'

export function Select({
  label,
  error,
  value,
  onChange,
  options,
  placeholder,
  disabled = false,
  required = false,
  className,
  ...props
}: SelectProps & Omit<React.SelectHTMLAttributes<HTMLSelectElement>, 'onChange'>) {
  const id = useId()

  return (
    <div className="space-y-1">
      {label && (
        <label 
          htmlFor={id}
          className="block text-sm font-medium text-slate-700 dark:text-slate-300"
        >
          {label}
          {required && <span className="text-red-500 ml-1">*</span>}
        </label>
      )}
      
      <div className="relative">
        <select
          id={id}
          value={value}
          onChange={(e) => onChange?.(e.target.value)}
          disabled={disabled}
          required={required}
          className={cn(
            'input appearance-none pr-10',
            {
              'border-red-300 dark:border-red-600 focus:ring-red-500': error,
            },
            className
          )}
          {...props}
        >
          {placeholder && (
            <option value="" disabled>
              {placeholder}
            </option>
          )}
          {options.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
        
        <ChevronDown className="absolute right-3 top-1/2 transform -translate-y-1/2 w-4 h-4 text-slate-500 pointer-events-none" />
      </div>
      
      {error && (
        <p className="text-sm text-red-600 dark:text-red-400">
          {error}
        </p>
      )}
    </div>
  )
}